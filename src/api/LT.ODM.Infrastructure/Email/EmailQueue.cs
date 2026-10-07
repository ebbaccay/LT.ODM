using System.Net;
using System.Net.Mail;
using System.Threading.Channels;
using LT.ODM.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LT.ODM.Infrastructure.Email;

/// <summary>In-memory queue; <see cref="EmailSenderService"/> delivers in the background.</summary>
public sealed class EmailQueue : IEmailQueue
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public ValueTask QueueAsync(EmailMessage message, CancellationToken ct = default) => _channel.Writer.WriteAsync(message, ct);
}

public sealed class EmailSenderService(EmailQueue queue, IOptions<EmailOptions> options, ILogger<EmailSenderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Sending email \"{Subject}\" failed.", message.Subject);
            }
        }
    }

    private async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Host))
        {
            if (o.LogBodyWhenNotConfigured)
                logger.LogInformation("SMTP not configured. Email to {To}: {Subject}\n{Body}", message.To, message.Subject, message.TextBody);
            else
                logger.LogWarning("SMTP not configured (Email:Host); email \"{Subject}\" was not sent.", message.Subject);
            return;
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(o.FromAddress, o.FromName),
            Subject = message.Subject,
            Body = message.TextBody,
        };
        mail.To.Add(message.To);
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, "text/html"));

        using var client = new SmtpClient(o.Host, o.Port) { EnableSsl = o.EnableSsl };
        if (!string.IsNullOrEmpty(o.UserName))
            client.Credentials = new NetworkCredential(o.UserName, o.Password);

        await client.SendMailAsync(mail, ct);
    }
}
