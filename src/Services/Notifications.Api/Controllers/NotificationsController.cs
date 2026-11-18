using BuildingBlocks.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Notifications.Api.Controllers;

/// <summary>Logged-in users only. Customers see the emails sent to them; admins see all.</summary>
[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
[Authorize]
public class NotificationsController(NotificationStore store) : ControllerBase
{
    /// <summary>The most recent "emails", newest first. Filter with ?orderId=...</summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<Notification>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IEnumerable<Notification> GetNotifications([FromQuery] Guid? orderId)
    {
        IEnumerable<Notification> visible = store.GetRecent();

        if (!User.IsAdmin())
        {
            // Notifications has no users table either: the email in the token is the customer's identity.
            var email = User.GetEmail();
            visible = visible.Where(n => string.Equals(n.To, email, StringComparison.OrdinalIgnoreCase));
        }

        return orderId is null ? visible : visible.Where(n => n.OrderId == orderId);
    }
}
