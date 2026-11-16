"""Shared fixtures of the AI service's tests."""

import pytest

import chat as chat_module


@pytest.fixture
def no_cards(monkeypatch):
    """A turn whose tool draws no card (UX-01 PR5 K2-A): for the tests whose subject is the model's explanation, which runs only
    for an answer without a card. The summary tool these tests use has had a card since PR4."""
    monkeypatch.setattr(chat_module, "cards_for", lambda tool, arguments, result: [])
