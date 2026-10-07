namespace LT.ODM.Infrastructure.Email;

/// <summary>Bound from the "Email" configuration section (SMTP relay, e.g. the on-prem Exchange server).</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>SMTP host. When empty, emails are not sent (see <see cref="LogBodyWhenNotConfigured"/>).</summary>
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 25;
    public bool EnableSsl { get; set; } = true;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "LT ODM Style Library";
    /// <summary>Optional; leave empty for an anonymous / IP-authorised relay.</summary>
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Development only: write unsent emails (including reset links) to the log.</summary>
    public bool LogBodyWhenNotConfigured { get; set; }
}
