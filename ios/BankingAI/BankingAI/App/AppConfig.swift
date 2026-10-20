import Foundation

/// Where the app finds the API.
enum AppConfig {
    /// The local API (`dotnet run --project src/BankingAI.Api`). The simulator shares the Mac's network, so `localhost`
    /// is the Mac; plain HTTP is allowed for local addresses only (Info.plist: NSAllowsLocalNetworking).
    static let apiBaseURL = URL(string: "http://localhost:5248/")!
}
