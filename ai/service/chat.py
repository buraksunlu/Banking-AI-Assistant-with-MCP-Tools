"""One chat turn: the model picks a tool, the backend computes, the model explains (AI-14a, MCP-b, CHAT-03, CHAT-02, CHAT-01,
CHAT-06).

    waiting question? → offered follow-up? → route (code; the model only without a matching rule) → tools (MCP server), narrowed to the route's
    group → tool selection (model, with the last call when the message is a follow-up) → grounding (code) → tool call (MCP
    server checks and computes) → facts (code) → explanation (model) → last call kept, follow-ups offered

A period, count or figure (budget amount, currency, cut; MCP-17/18) the user did not say, or a merchant name that fits several
merchants, is asked back instead of guessed (CHAT-03); the reply completes the remembered call without a second selection, and
a figure still unsaid after it is asked next. A short follow-up ("Peki geçen ay?") is read
with the last call; an argument must then be in the message or carried over unchanged (CHAT-02). A follow-up offered under
the last answer, sent back as it was written, runs the call it stands for without a tool selection (CHAT-06). The answer's figures are
never carried over: every turn is computed again. The whole turn shares one time limit. The
user's token only goes to the MCP server (and, as a digest, keys the waiting question), never to a model.
"""

import json
import logging
import time
from dataclasses import dataclass, field, replace
from typing import Protocol

from langchain_ollama import ChatOllama

from answer_check import has_advice, misnamed
from clarification import (CHOICE_KINDS, Pending, PendingStore, ask_amount, ask_complete_month, ask_count, ask_currency,
                           ask_merchant, ask_percent, ask_period, ask_scenario, ask_topic, check_question, chosen_tool,
                           is_bare_choice, resolve, template)
from contracts import Fact
from conversation import ConversationStore, LastCall, is_follow_up
from explainer import stream_message
from facts import outcome_for
from ollama_client import OllamaError, OllamaSettings
from prompts import (ADVICE_DECLINED_TEXT, CLARIFY_TEXT, DATE_SCHEMA, LIRA_ONLY_TEXT, INTENT_SCHEMA, OUT_OF_SCOPE_TEXT, build_clarify_messages, build_date_messages,
                     build_intent_messages, build_tool_messages)
from dates import CustomerDay, DateReading, check_range, period_line, read_dates, with_span_replaced
from grounding import category_in, check_arguments, mentions_period
from routing import DATE_FIELDS, ONE_OFF, PLANNING, Route, Router, group_of, hide_dates, is_spending_question, narrow, planning_of
from suggestions import OUT_OF_SCOPE_SUGGESTIONS, OfferedStore, follow_ups, match
from tools import ToolArgumentsRejected, ToolGateway
from wealth import (LIRA, MONTH_TOOLS, MONTHLY_TOOL, WEALTH_TOOLS, advised_saving_call, chosen_scenario_arguments, has_complete_month,
                    needs_month_question, plan_monthly, scenario_by_figures)

logger = logging.getLogger("bankingai.ai")


@dataclass(frozen=True)
class ToolCall:
    """A tool and its arguments, as the model chose them (and, after the check, as they were run)."""

    tool: str
    arguments: dict


@dataclass(frozen=True)
class ChatReply:
    """The answer of one turn; ``type`` is "answer", "clarification" or "outOfScope"; ``suggestions`` are the questions to
    offer under it (CHAT-06)."""

    type: str
    answer: str
    tool_calls: list[ToolCall]
    facts: list[Fact]
    suggestions: list[str] = field(default_factory=list)


class ToolSelector(Protocol):
    """Lets the model choose among the given tools for a message."""

    def select(self, message: str, tools: list[dict], deadline: float, context: LastCall | None = None) -> list[ToolCall]: ...


# K8-A: a tool call is 30-60 tokens. Without a cap, a model that writes a text answer instead (a joke) can run past the
# turn's limit; that text is never shown (the fixed out-of-scope reply is), so cutting it loses nothing.
MAX_TOOL_CALL_TOKENS = 200
# A one-sentence question with two or three options is 40-80 tokens (CHAT-03 probe: 1,3-2,0 s).
MAX_QUESTION_TOKENS = 120
# The question is a nicety: when the model is slow, the fixed question is shown instead of keeping the user waiting.
QUESTION_SECONDS = 8.0
# {"intent": "payments"} is about 10 tokens; the cap keeps a model that starts writing prose from using the turn.
MAX_INTENT_TOKENS = 30
# {"understood": true, "from": "2026-10-06", "to": "2026-10-06"} is about 35 tokens.
MAX_DATE_TOKENS = 60
# The second step for a date is a nicety too: a slow model means the period is asked instead.
DATE_SECONDS = 5.0


class ClarificationWriter(Protocol):
    """Words a question back with the given options (CHAT-03 K4-B)."""

    def write(self, question: str, kind: str, options: list[str], numbered: bool, deadline: float, asked: str = "") -> str: ...


class LangChainToolSelector:
    """Tool selection with the local model through LangChain's tool calling."""

    def __init__(self, settings: OllamaSettings):
        self.settings = settings
        # Same model settings as the explainer (AI-01/04): no thinking, deterministic.
        self.llm = ChatOllama(
            model=settings.model, base_url=settings.base_url, reasoning=False, temperature=0, keep_alive=settings.keep_alive,
            num_predict=MAX_TOOL_CALL_TOKENS, client_kwargs={"timeout": settings.timeout_seconds},
        )
        self.clock = time.monotonic

    def select(self, message: str, tools: list[dict], deadline: float, context: LastCall | None = None) -> list[ToolCall]:
        """The tools the model chose (``context``: the previous call of a follow-up); an empty list when it answered in text."""
        # Bound per turn: the list comes from the MCP server on every turn, with the user's token (K1-A).
        reply = stream_message(self.llm.bind_tools(tools), build_tool_messages(message, context), deadline, self.clock, self.settings)
        return [ToolCall(call["name"], call["args"]) for call in reply.tool_calls]


class LangChainClarificationWriter:
    """The local model words the question; the chat checks it and falls back to the fixed one (K4-B)."""

    def __init__(self, settings: OllamaSettings):
        self.settings = settings
        self.llm = ChatOllama(
            model=settings.model, base_url=settings.base_url, reasoning=False, temperature=0, keep_alive=settings.keep_alive,
            num_predict=MAX_QUESTION_TOKENS, client_kwargs={"timeout": settings.timeout_seconds},
        )
        self.clock = time.monotonic

    def write(self, question: str, kind: str, options: list[str], numbered: bool, deadline: float, asked: str = "") -> str:
        """The question as the model words it (not yet checked); ``asked`` is the merchant text that matched several."""
        messages = build_clarify_messages(question, kind, options, numbered, asked)
        return str(stream_message(self.llm, messages, deadline, self.clock, self.settings).content).strip()


class LangChainIntentClassifier:
    """The tool group of a message no word rule matched (CHAT-01 K1-C), held to a JSON schema."""

    def __init__(self, settings: OllamaSettings):
        self.settings = settings
        self.llm = ChatOllama(
            model=settings.model, base_url=settings.base_url, reasoning=False, temperature=0, keep_alive=settings.keep_alive,
            num_predict=MAX_INTENT_TOKENS, format=INTENT_SCHEMA, client_kwargs={"timeout": settings.timeout_seconds},
        )
        self.clock = time.monotonic

    def classify(self, message: str, deadline: float) -> str | None:
        """The group the model chose; None when its answer is not the schema's shape (the router then uses its fallback)."""
        text = str(stream_message(self.llm, build_intent_messages(message), deadline, self.clock, self.settings).content)
        try:
            intent = json.loads(text).get("intent")
        except (json.JSONDecodeError, AttributeError):
            return None
        return intent if isinstance(intent, str) else None


class LangChainDateInterpreter:
    """The second step for a date expression code could not read (CHAT-08 K1-C): the model gives a range, code checks it."""

    def __init__(self, settings: OllamaSettings):
        self.settings = settings
        self.llm = ChatOllama(
            model=settings.model, base_url=settings.base_url, reasoning=False, temperature=0, keep_alive=settings.keep_alive,
            num_predict=MAX_DATE_TOKENS, format=DATE_SCHEMA, client_kwargs={"timeout": settings.timeout_seconds},
        )
        self.clock = time.monotonic

    def interpret(self, expression: str, day: CustomerDay, deadline: float) -> DateReading | None:
        """The checked range ("found", or "invalid" with a reason); None when the model did not understand or did not follow the shape."""
        text = str(stream_message(self.llm, build_date_messages(expression, day), deadline, self.clock, self.settings).content)
        try:
            answer = json.loads(text)
        except json.JSONDecodeError:
            return None
        if not isinstance(answer, dict) or answer.get("understood") is not True:
            return None
        start, end = answer.get("from"), answer.get("to")
        if not isinstance(start, str) or not isinstance(end, str):
            return None
        return check_range(start, end, day.today)


class DateInterpreter(Protocol):
    """Turns a date expression into a checked range for the customer's day."""

    def interpret(self, expression: str, day: CustomerDay, deadline: float) -> DateReading | None: ...


class ChatService:
    """Runs one chat turn."""

    def __init__(self, selector: ToolSelector, gateway: ToolGateway, explainer, settings: OllamaSettings,
                 writer: ClarificationWriter | None = None, waiting: PendingStore | None = None,
                 history: ConversationStore | None = None, router: Router | None = None, offered: OfferedStore | None = None,
                 interpreter: DateInterpreter | None = None):
        self.selector, self.gateway, self.explainer, self.settings = selector, gateway, explainer, settings
        # Without a classifier the router uses its rules and the spending group (tests; CHAT-01 K3-A).
        self.router = router or Router()
        # Without a writer every question is the fixed one (tests, and the fallback of K4-B).
        self.writer = writer
        self.waiting = waiting or PendingStore()
        self.history = history or ConversationStore()
        self.offered = offered or OfferedStore()
        # Without one a date code cannot read is asked back (tests, and the fallback of K1-C).
        self.interpreter = interpreter
        self.clock = time.monotonic

    def answer(self, message: str, token: str, day: CustomerDay | None = None) -> ChatReply:
        """The reply to ``message`` for the user whose token is given; ``day`` is the customer's today and time zone (CHAT-08)."""
        # One limit for the whole turn: listing, selection, tool call and explanation all take from it (AI-06 K5-B, AI-14).
        deadline = self.clock() + self.settings.timeout_seconds

        # A reply to the question asked last time completes that call: no listing, no selection (K3-A). Anything else is a
        # new question, and the old one is dropped either way (K5-A).
        waiting = self.waiting.take(token)
        # Like the waiting question, an offer lasts for one message: whatever comes next, it is gone.
        offered = self.offered.take(token)
        completed = False
        # CHAT-09 K17-C/K19-C: the tool the user chose between two calculations; their question then goes the normal way with
        # that tool alone, and its own words and figures (plus the reply's, "her ay 2 bin") are checked as always.
        forced = None
        if waiting is not None and waiting.kind in CHOICE_KINDS:
            forced = chosen_tool(waiting, message)
            if forced is not None:
                message = waiting.original if is_bare_choice(message) else f"{waiting.original} ({message})"
                completed = True
        elif waiting is not None and waiting.tool == "":
            # A period asked before any tool was chosen (K12-C): the reply is added to the question and the whole goes the
            # normal way, dates and all.
            phrase = _period_phrase(waiting, message, day)
            if phrase is not None:
                message, completed = f"{waiting.original} ({phrase})", True
        elif waiting is not None:
            if waiting.kind == "period" and day is not None:
                reading = self._read(message, day, deadline)
                if reading.kind in ("invalid", "unreadable") and _is_reply(message, reading):
                    pending = ask_period(waiting.tool, waiting.arguments, waiting.question, note=_note(reading))
                    return self._ask(pending, [], token, deadline)
                date_arguments = _date_arguments(waiting, message, reading)
            else:
                date_arguments = None
            arguments = date_arguments or resolve(waiting, message)
            if arguments is not None:
                question = f"{waiting.question} ({message})"
                # "3000" answered the amount; the currency the model guessed as well is asked next, not run.
                later = tuple(name for name in waiting.later if name not in arguments)
                if later:
                    return self._ask(_ask_figure(waiting.tool, arguments, later[0], later[1:], question), [], token, deadline)
                return self._run(ToolCall(waiting.tool, arguments), question, token, deadline, day)

        # The text of an offered follow-up stands for the call it was built from (CHAT-06 K3-C): code chose the tool, so the
        # model is not asked again; the MCP server still checks the arguments.
        chosen = match(offered, message)
        if chosen is not None:
            return self._run(ToolCall(chosen.tool, dict(chosen.arguments)), message, token, deadline, day)

        # Only a short or "peki…" message is read with the last call (K4-B); a full question stands on its own, as before.
        # The follow-up itself is what is explained and kept: a growing chain of questions led both the model (back to an
        # older merchant) and the explanation ("geçen ay" for this month) astray in the live trial.
        context = self.history.recall(token) if not completed and is_follow_up(message) else None
        if forced is not None:
            route = Route((group_of(forced),), only=forced)
        else:
            route = self.router.route(message, context.tool if context else None, deadline)
        if route.out_of_scope:
            # A request to move money or to advise is answered before any tool or model is involved (CHAT-01 K5-B).
            return _advice_declined() if route.advice else _out_of_scope()

        # "Had I saved the amount you advised": code chooses the call from the words, the model never picked it (INC-03 PR2, W12/W13).
        if (advised := advised_saving_call(message, context)) is not None:
            return self._run(ToolCall(*advised), message, token, deadline, day)

        # The dates the user wrote are read by code (CHAT-08): the model is shown a period name in their place and never
        # chooses a date (ANA-20); the range is put into the call below.
        reading = self._read(message, day, deadline) if day is not None else DateReading("none")
        if reading.kind in ("invalid", "unreadable"):
            return self._ask(ask_period("", {}, message, note=_note(reading), original=_without(message, reading.span)), [], token, deadline)
        shown = with_span_replaced(message, reading.span, "bu ay") if reading.kind == "found" else message
        context, carried = _mask_range(context)
        if route.undecided is not None:
            # CHAT-09 K17-C/K18-B: the model is not let guess between the two "what if"s; with no scenario word the user's figures
            # may still tell (a period: the one-off, an amount: the monthly saving), else the user is asked.
            figured = scenario_by_figures(shown) if route.undecided == "neither" else None
            if figured is None:
                return self._ask(ask_scenario(message), [], token, deadline)
            route = replace(route, only=figured, undecided=None)

        # Listed first: when the tools cannot answer, the single local model is not kept busy for nothing.
        listed = narrow(self.gateway.list_tools(token, deadline), route)
        calls = self.selector.select(shown, hide_dates(listed), deadline, context)
        if not calls and route.groups == (PLANNING,):
            # CHAT-09 K19-C: the model chose nothing from the planning list; the words (or the user's choice) name the tool, its
            # figures are then asked as always. Words that name both or neither are asked as a choice.
            tool = route.only or planning_of(shown)
            if tool is None:
                return self._ask(ask_topic(message), [], token, deadline)
            calls = [ToolCall(tool, {"categoryCode": category} if (category := category_in(shown)) else {})]
        if not calls and route.only is not None and any(t["function"]["name"] == route.only for t in listed):
            # CHAT-09 K13-A: the words chose the scenario and the model was shown it alone; choosing nothing there is the small
            # model's indecision, not an out-of-scope question. Its arguments are read from the words, and checked below as always.
            arguments = chosen_scenario_arguments(route.only, shown)
            if arguments is None:
                known = {"categoryCode": category} if (category := category_in(shown)) else {}
                return self._ask(ask_period(route.only, known, message), [], token, deadline)
            calls = [ToolCall(route.only, arguments)]
        if not calls:
            if is_spending_question(message) and not mentions_period(shown):
                # A spending question whose period is missing is not out of scope: it is asked (CHAT-08 K6-C).
                return self._ask(ask_period("", {}, message, original=message), [], token, deadline)
            # The model's own text is never shown here: it might promise a transfer it cannot make (K5-A).
            return _out_of_scope()
        if len(calls) > 1:
            # Running only one would show half an answer as a whole one (K2-A); several tools come with CHAT-04.
            return ChatReply("clarification", CLARIFY_TEXT, calls, [])

        # A date the model wrote although it was not shown the field is not the user's word.
        call = ToolCall(calls[0].tool, {k: v for k, v in calls[0].arguments.items() if k not in DATE_FIELDS})
        schemas = {tool["function"]["name"]: tool["function"].get("parameters", {}) for tool in listed}
        if call.tool not in schemas:
            return ChatReply("clarification", CLARIFY_TEXT, calls, [])
        takes_dates = "from" in schemas[call.tool].get("properties", {})
        if reading.kind == "found" and not takes_dates:
            # Running it without the dates would answer for another period (K11-B).
            return _date_unsupported()

        # INC-03: the monthly saving is built from the user's own words, and a complete-month analysis asks for the month it cannot
        # guess; both before the general check, which would take the model's LastMonth or amount for the user's.
        if call.tool == MONTHLY_TOOL:
            return self._monthly(call, shown, context, token, deadline, day)
        if call.tool in MONTH_TOOLS and reading.kind != "found" and needs_month_question(shown, call.arguments, context):
            return self._ask(ask_complete_month(call.tool, {}, message, day.today if day else None), [], token, deadline)

        # A period or count the user did not say is the model's guess: asked back, the tool does not run (K1-C, K2-A). With a date
        # read by code the period is code's own (below): the "bu ay" put in the date's place is no period the user named (CHAT-09 K12-A).
        checked = {k: v for k, v in call.arguments.items() if not (reading.kind == "found" and k == "period")}
        grounding = check_arguments(checked, schemas[call.tool], shown, context.arguments if context else None)
        if grounding.missing is not None:
            return self._ask(self._question_for(call, grounding.unsaid, schemas[call.tool], message), [], token, deadline)
        arguments = grounding.arguments
        if call.tool == ONE_OFF and "categoryCode" not in arguments and (category := category_in(shown)) is not None:
            # CHAT-09 (model run of PR2, W07): the category the user named and the model left out; without it all spending of the
            # period would be bought. Read from the words, as K13-A reads them.
            arguments = {**arguments, "categoryCode": category}
        if reading.kind == "found":
            arguments = {**arguments, "period": "Custom", "from": reading.start.isoformat(), "to": reading.end.isoformat()}
        elif carried and takes_dates and arguments.get("period") == MASKED_PERIOD and not mentions_period(message):
            # The follow-up kept the period it was shown ("bu ay"), so it means the range of the last call (CHAT-08 K7-A).
            arguments = {**arguments, "period": "Custom", **carried}
        return self._run(ToolCall(call.tool, arguments), message, token, deadline, day)

    def _monthly(self, call: ToolCall, message: str, context: LastCall | None, token: str, deadline: float,
                 day: CustomerDay | None) -> ChatReply:
        """The monthly saving: what the user's words allow (K8-A, K9-A, K17-B), asked back or refused otherwise."""
        plan = plan_monthly(call.arguments, message, after_advice=context is not None and context.tool == "get_spending_advice")
        if plan.lira_only:
            return _lira_only()
        if plan.ask is not None:
            return self._ask(_ask_figure(call.tool, plan.known, plan.ask, plan.later, message), [], token, deadline)
        return self._run(ToolCall(call.tool, plan.run), message, token, deadline, day)

    def _read(self, message: str, day: CustomerDay, deadline: float) -> DateReading:
        """The dates in ``message``: by code, and for an expression code cannot read the model's range, checked by code again."""
        reading = read_dates(message, day.today)
        if reading.kind != "unreadable" or self.interpreter is None:
            return reading
        try:
            interpreted = self.interpreter.interpret(message[slice(*reading.span)], day, min(deadline, self.clock() + DATE_SECONDS))
        except OllamaError as error:
            logger.info("No model reading of a date (%s); the period is asked", type(error).__name__)
            return reading
        return reading if interpreted is None else replace(interpreted, span=reading.span)

    def _run(self, call: ToolCall, question: str, token: str, deadline: float, day: CustomerDay | None = None) -> ChatReply:
        if call.tool == MONTHLY_TOOL and call.arguments.get("currencyCode") not in (None, LIRA):
            # The currency can also arrive as the reply to "which currency?": every asset is quoted in lira (INV-01 K15-B).
            return _lira_only()
        if call.tool in MONTH_TOOLS and day is not None and not has_complete_month(call.arguments, day.today):
            # A reply such as "bu ay", or a range that is not a whole month, would be refused by the server (INC-01 K2-A).
            return self._ask(ask_complete_month(call.tool, {}, question, day.today), [], token, deadline)
        try:
            # The MCP server checks the arguments against the schema it published (MCP-a K3-A); there is no second copy here.
            result = self.gateway.call(call.tool, call.arguments, token, deadline)
        except ToolArgumentsRejected:
            # The same fixed question as before MCP (MCP-b K3-A): the user sees no difference.
            return ChatReply("clarification", CLARIFY_TEXT, [call], [])
        outcome = outcome_for(call.tool, call.arguments, result)
        used = [call]
        if outcome.candidates:
            # The backend found several of the customer's merchants: one of them is chosen by the user, never here.
            pending = ask_merchant(call.tool, call.arguments, call.arguments.get("merchant", ""), list(outcome.candidates), question)
            return self._ask(pending, used, token, deadline)
        if outcome.reply is not None:
            kind, text = outcome.reply
            return ChatReply(kind, text, used, outcome.facts)

        # CHAT-09 K7-A: a what-if is explained from the question code wrote, not from the user's words and replies.
        answer = self.explainer.explain(outcome.question or question, outcome.facts, deadline=deadline)
        if call.tool in WEALTH_TOOLS and (has_advice(answer) or misnamed(answer, outcome.named_values)):
            # INC-03 K7-A: the model must not say what to buy or what a price will do; CHAT-09 K16-A: nor leave the reader to guess
            # which value is whose. The figures alone stand.
            logger.info("Explanation of %s sounded like advice or mixed up the assets; the facts are shown instead", call.tool)
            answer = "\n".join(f"{fact.label}: {fact.text}" for fact in outcome.facts)
        # After the answer check, by code: the user sees which days were meant, whatever the model wrote (CHAT-08 K16-A).
        if (line := period_line(call.arguments)) is not None:
            answer = f"{line}\n\n{answer}"
        # K6-A, K18-C: the backend's own sentences (the investment warning, the advice) go under the answer, written by code.
        if outcome.appendix:
            answer = "\n\n".join([answer, *outcome.appendix])
        # Kept only after an explained answer: the call, never its figures (CHAT-02 K2-A).
        self.history.remember(token, LastCall(call.tool, call.arguments, question))
        # Only under an explained answer (K6-B): under a question asked back they would lead away from it.
        suggestions = follow_ups(call.tool, call.arguments)
        self.offered.put(token, suggestions)
        return ChatReply("answer", answer, used, outcome.facts, [s.text for s in suggestions])

    @staticmethod
    def _question_for(call: ToolCall, unsaid: tuple[str, ...], schema: dict, message: str) -> Pending:
        argument, later = unsaid[0], unsaid[1:]
        # Every guessed value is left out of the stored call, so none of them runs once the user has answered.
        known = {k: v for k, v in call.arguments.items() if k not in unsaid}
        if argument == "period":
            return ask_period(call.tool, known, message)
        if argument in ("amount", "currencyCode", "reductionPercent"):
            return _ask_figure(call.tool, known, argument, later, message)
        rule = schema.get("properties", {}).get(argument, {})
        return ask_count(call.tool, known, argument, rule.get("minimum", 1), rule.get("maximum", 12), message)

    def _ask(self, pending: Pending, used: list[ToolCall], token: str, deadline: float) -> ChatReply:
        text = template(pending)
        # A figure question stays the fixed text: a worded one could suggest an amount or a cut (MCP-17/18).
        if self.writer is not None and pending.model_worded:
            try:
                written = self.writer.write(pending.question, pending.kind, [o.label for o in pending.options], pending.numbered,
                                            min(deadline, self.clock() + QUESTION_SECONDS), pending.asked)
                if check_question(written, pending):
                    text = written
                else:
                    # Only the kind is logged: the wording may repeat the user's words.
                    logger.info("Model question failed the check (%s); the fixed question is used", pending.kind)
            except OllamaError as error:
                logger.info("No model question (%s); the fixed question is used", type(error).__name__)
        self.waiting.put(token, pending)
        return ChatReply("clarification", text, used, [])


# The period a follow-up is shown instead of the last call's date range (CHAT-08 K7-A): a name the model was measured with.
MASKED_PERIOD = "ThisMonth"

DATE_UNSUPPORTED_TEXT = ("Bu soruyu belirli bir tarih ya da tarih aralığı için cevaplayamıyorum. Belirli tarihler için toplam harcamanıza, "
                         "kategorilere, işyerlerine ve en büyük harcamalarınıza bakabilirim; diğer sorularda bu ay, geçen ay gibi "
                         "dönemleri kullanabilirsiniz.")


def _date_unsupported() -> ChatReply:
    # Fixed text and the same questions every customer gets an answer to (K11-B); no tool ran, so no figure is in it.
    return ChatReply("outOfScope", DATE_UNSUPPORTED_TEXT, [], [], list(OUT_OF_SCOPE_SUGGESTIONS))


def _note(reading: DateReading) -> str:
    return reading.reason if reading.kind == "invalid" else "Tarihi anlayamadım."


def _without(message: str, span: tuple[int, int] | None) -> str:
    """The question without its date words, to which the reply to "which period?" is added; a message that was only a date asks
    about spending."""
    rest = " ".join((with_span_replaced(message, span, "") if span else message).split())
    return rest or "Ne kadar harcadım?"


def _is_reply(message: str, reading: DateReading) -> bool:
    # A reply is the date and at most a word or two ("1-5 Eylül olsun"); a longer message is a question of its own.
    return len(with_span_replaced(message, reading.span, "").split()) <= 2


def _period_phrase(waiting: Pending, message: str, day: CustomerDay | None) -> str | None:
    """The reply to a period asked before any tool, as words to add to the question; None when it is not an answer."""
    if day is not None:
        reading = read_dates(message, day.today)
        if reading.kind != "none" and _is_reply(message, reading):
            return message.strip()
    arguments = resolve(waiting, message)
    if arguments is None:
        return None
    # A number stands for the option it numbers; a typed period is its own words.
    label = next((option.label for option in waiting.options if option.value == arguments[waiting.argument]), None)
    return label or message.strip()


def _date_arguments(waiting: Pending, message: str, reading: DateReading) -> dict | None:
    """The completed call when a date is the reply to a period question of a chosen tool; None for any other reply."""
    if reading.kind != "found" or not _is_reply(message, reading):
        return None
    return {**waiting.arguments, "period": "Custom", "from": reading.start.isoformat(), "to": reading.end.isoformat()}


def _mask_range(context: LastCall | None) -> tuple[LastCall | None, dict | None]:
    """The last call as the model is shown it, and the range to restore. A range is shown as a named period: the model never
    sees a date, and can only keep it ("Peki işyerlerine göre?") or replace it by a period the user names."""
    if context is None or context.arguments.get("period") != "Custom":
        return context, None
    carried = {k: context.arguments[k] for k in DATE_FIELDS if k in context.arguments}
    shown = {k: v for k, v in context.arguments.items() if k not in DATE_FIELDS}
    return LastCall(context.tool, {**shown, "period": MASKED_PERIOD}, context.question), carried


def _advice_declined() -> ChatReply:
    # K5-A: a request for advice or a forecast; the same three questions every customer gets an answer to.
    return ChatReply("outOfScope", ADVICE_DECLINED_TEXT, [], [], list(OUT_OF_SCOPE_SUGGESTIONS))


def _lira_only() -> ChatReply:
    # K8-A: a monthly saving in another currency; no tool ran, so no figure is in it.
    return ChatReply("answer", LIRA_ONLY_TEXT, [], [], [])


def _out_of_scope() -> ChatReply:
    # Full questions every customer gets an answer to (K6-B); they take the normal path, so they are not remembered.
    return ChatReply("outOfScope", OUT_OF_SCOPE_TEXT, [], [], list(OUT_OF_SCOPE_SUGGESTIONS))


def _ask_figure(tool: str, arguments: dict, argument: str, later: tuple[str, ...], question: str) -> Pending:
    """The question for one of the user's own figures (MCP-17/18), with the ones still to ask after it."""
    asker = {"amount": ask_amount, "currencyCode": ask_currency, "reductionPercent": ask_percent}[argument]
    return asker(tool, arguments, question, later=later)
