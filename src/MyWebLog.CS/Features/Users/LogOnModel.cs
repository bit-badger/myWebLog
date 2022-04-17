using System.ComponentModel.DataAnnotations;

namespace MyWebLog.Features.Users;

/// <summary>
/// The model to use to allow a user to log on
/// </summary>
public class LogOnModel : MyWebLogModel
{
    /// <summary>
    /// The user's e-mail address
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    [Display(ResourceType = typeof(Resources), Name = "EmailAddress")]
    public string EmailAddress { get; set; } = "";
    
    /// <summary>
    /// The user's password
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    [Display(ResourceType = typeof(Resources), Name = "Password")]
    public string Password { get; set; } = "";

    [Obsolete("Only used for model binding; use the WebLogDetails constructor")]
    public LogOnModel() : base(new()) { }

    /// <inheritdoc />
    public LogOnModel(WebLogDetails webLog) : base(webLog) { }
}
