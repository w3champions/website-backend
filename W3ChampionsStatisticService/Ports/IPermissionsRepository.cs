using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using W3C.Contracts.Admin.Permission;

namespace W3ChampionsStatisticService.Ports;

public interface IPermissionsRepository
{
    Task<List<Permission>> GetPermissions(string authorization);
    Task<HttpStatusCode> AddAdmin(Permission permission, string authorization);
    Task<HttpStatusCode> EditAdmin(Permission permission, string authorization);
    Task<HttpStatusCode> DeleteAdmin(string id, string authorization);
}
