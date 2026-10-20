import Foundation

/// Why a request to the API did not produce the expected answer, in the API's own terms (docs/api.md, API-13).
enum APIError: Error, Equatable {
    /// An error answer with the API's problem body: status, machine code, Turkish title and, for 429, the wait.
    case problem(status: Int, code: String, title: String, retryAfterSeconds: Int?)
    /// The API could not be reached: not running, wrong address or no network.
    case unreachable
    /// The request was cancelled by the app; there is nothing to show.
    case cancelled
    /// An answer outside the contract: an error without a problem body, or JSON that does not decode.
    case invalidResponse(status: Int)
}

extension APIError {
    /// A Turkish sentence for the screen. The API's titles are already Turkish and written for people (API-13).
    var message: String {
        switch self {
        case .problem(status: 429, _, _, let seconds?):
            "Çok fazla deneme; \(seconds) sn sonra tekrar deneyin."
        case .problem(_, _, let title, _):
            title
        case .unreachable:
            "Sunucuya ulaşılamıyor; bağlantıyı kontrol edip tekrar deneyin."
        case .cancelled:
            "İstek durduruldu."
        case .invalidResponse:
            "Beklenmeyen bir cevap alındı; tekrar deneyin."
        }
    }
}
