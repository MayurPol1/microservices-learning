using Microsoft.AspNetCore.Mvc;

namespace Notifications.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
public class NotificationsController(NotificationStore store) : ControllerBase
{
    /// <summary>The most recent "emails", newest first. Filter with ?orderId=...</summary>
    [HttpGet]
    [ProducesResponseType<IEnumerable<Notification>>(StatusCodes.Status200OK)]
    public IEnumerable<Notification> GetNotifications([FromQuery] Guid? orderId)
    {
        var recent = store.GetRecent();
        return orderId is null ? recent : recent.Where(n => n.OrderId == orderId);
    }
}
