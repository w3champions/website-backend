using System;
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
    Task<IgnoredIdentifier> GetIgnoredIdentifier(string type, string identifier);
    Task<List<IgnoredIdentifier>> GetIgnoredIdentifiers(string type, string continuationToken);
    Task<IgnoredIdentifier> AddIgnoredIdentifier(string type, string identifier, string reason, string author);
    Task<HttpStatusCode> DeleteIgnoredIdentifier(string id);
    Task<List<string>> GetPossibleIdentifierTypes();
    Task<SmurfDetectionResult> QuerySmurfsFor(string identifierType, string identifier, bool includeExplanation, int iterationDepth);
}
