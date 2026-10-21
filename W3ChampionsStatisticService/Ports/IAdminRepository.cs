using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using W3ChampionsStatisticService.Admin;
using W3ChampionsStatisticService.Admin.SmurfDetection;

namespace W3ChampionsStatisticService.Ports;

public interface IAdminRepository
{
    Task<List<ProxiesResponse>> GetProxies();
    Task<ProxyUpdate> UpdateProxies(ProxyUpdate proxyUpdateData, string battleTag);
    Task<FloProxies> GetProxiesFor(string battleTag);
    Task<List<string>> SearchSmurfsFor(string battleTag);
    Task<GlobalChatBanResponse> GetChatBans(string query, string nextId);
    Task<HttpStatusCode> PutChatBan(ChatBanPutDto chatBan);
    Task<HttpStatusCode> DeleteChatBan(string id);
    Task<List<IgnoredIdentifier>> GetIgnoredIdentifiers(string type, string continuationToken);
    Task<IgnoredIdentifier> AddIgnoredIdentifier(string type, string identifier, string reason, string author);
    Task<HttpStatusCode> DeleteIgnoredIdentifier(string id);
    Task<List<string>> GetPossibleIdentifierTypes();
    Task<SmurfDetectionResult> QuerySmurfsFor(string identifierType, string identifier, bool includeExplanation, int iterationDepth);

    Task<MatchmakingProxyResult> GetHardwareLinks(string battleTag);
    Task<MatchmakingProxyResult> GetHardwareItems(string battleTag);
    Task<MatchmakingProxyResult> GetHardwareItem(string battleTag, string hash8);
    Task<MatchmakingProxyResult> GetHardwareAudit(string battleTag, string before, int? limit);
    Task<MatchmakingProxyResult> SetSmurfVerdict(string battleTagA, string battleTagB, string reason, string author);
    Task<MatchmakingProxyResult> RevokeSmurfVerdict(string battleTagA, string battleTagB, string author);
    Task<MatchmakingProxyResult> SetNotSmurfVerdict(string battleTagA, string battleTagB, string reason, string author);
    Task<MatchmakingProxyResult> RevokeNotSmurfVerdict(string battleTagA, string battleTagB, string author);
    Task<MatchmakingProxyResult> TrustHardwareItem(string battleTag, string hash8, string reason, string author);
    Task<MatchmakingProxyResult> RevokeHardwareItemTrust(string battleTag, string hash8, string author);
    Task<MatchmakingProxyResult> IgnoreHardwareItem(string battleTag, string hash8, string reason, string author);
    Task<MatchmakingProxyResult> RevokeHardwareItemIgnore(string battleTag, string hash8, string author);
}
