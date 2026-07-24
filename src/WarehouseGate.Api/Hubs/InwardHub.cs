using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WarehouseGate.Api.Hubs;

[Authorize]
public class InwardHub : Hub
{
    public const string SupervisorsGroup = "Supervisors";
    public const string SecurityGroup = "Security";
    public const string OfficeGroup = "Office";
    public const string LogisticsGroup = "Logistics";
    public const string AdminsGroup = "Admins";

    public override async Task OnConnectedAsync()
    {
        var role = Context.User?.FindFirstValue(ClaimTypes.Role);
        if (role == "Supervisor")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SupervisorsGroup);
        }
        else if (role == "Security")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, SecurityGroup);
        }
        else if (role == "Office")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, OfficeGroup);
        }
        else if (role == "LogisticsManager")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, LogisticsGroup);
        }
        else if (role == "SuperAdmin")
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        }

        await base.OnConnectedAsync();
    }
}
