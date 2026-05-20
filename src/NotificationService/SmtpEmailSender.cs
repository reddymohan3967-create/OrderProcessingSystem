using MailKit.Security;
using MimeKit;
using System.Text.RegularExpressions;

namespace NotificationService;

/// <summary>
/// Sends email messages using SMTP via MailKit. The implementation reads
/// SMTP connection settings from configuration and retries transient errors.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;

    /// <summary>
    /// Creates a new instance of <see cref="SmtpEmailSender"/>.
    /// </summary>
    /// <param name="config">Application configuration used to read SMTP settings.</param>
    /// <param name="logger">Logger for diagnostic messages.</param>
    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Sends an email message asynchronously using SMTP. The method will attempt
    /// to detect HTML content and set the message body appropriately. It also
    /// performs a small retry loop with exponential backoff for transient SMTP errors.
    /// </summary>
    /// <param name="to">Recipient email address.</param>
    /// <param name="subject">Email subject.</param>
    /// <param name="body">Email body. May contain HTML markup.</param>
    /// <param name="cancellationToken">Token to cancel the send operation.</param>
    /// <returns>A task that completes when the email has been sent or all retries fail.</returns>
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        // Read SMTP configuration from app configuration (appsettings.json, env, or user-secrets)
        var section = _config.GetSection("Smtp");
        var host =   "smtp.gmail.com";
        var port =  587;
        var useStartTls = true;
        var username = section["Username"] ?? string.Empty;
        var password = section["Password"] ?? string.Empty;
        var from = section["Username"] ?? string.Empty;

        var message = new MimeMessage();
        try
        {
            message.From.Add(MailboxAddress.Parse(from));
        }
        catch
        {
            message.From.Add(new MailboxAddress(from, from));
        }

        try
        {
            message.To.Add(MailboxAddress.Parse(to));
        }
        catch
        {
            message.To.Add(new MailboxAddress(to, to));
        }

        message.Subject = subject;

        var builder = new BodyBuilder();
        // Detect simple HTML content and populate HtmlBody/TextBody accordingly so
        // email clients render the HTML template instead of showing raw HTML.
        var isHtml = !string.IsNullOrWhiteSpace(body) && (
            body.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0 ||
            body.IndexOf("<body", StringComparison.OrdinalIgnoreCase) >= 0 ||
            body.IndexOf("<table", StringComparison.OrdinalIgnoreCase) >= 0 ||
            body.IndexOf("<div", StringComparison.OrdinalIgnoreCase) >= 0 ||
            body.IndexOf("<p", StringComparison.OrdinalIgnoreCase) >= 0 ||
            body.IndexOf("<br", StringComparison.OrdinalIgnoreCase) >= 0);

        if (isHtml)
        {
            builder.HtmlBody = body;
            // Basic plain-text fallback by removing tags
            try
            {
                var text = Regex.Replace(body, "<[^>]+>", string.Empty);
                builder.TextBody = text;
            }
            catch
            {
                builder.TextBody = string.Empty;
            }
        }
        else
        {
            builder.TextBody = body;
        }

        message.Body = builder.ToMessageBody();

        // Retry loop for transient SMTP errors
        var maxAttempts = 3;
        var rand = new Random();
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            MailKit.Net.Smtp.SmtpClient client = null;
            try
            {
                // Optionally enable protocol logging by setting Smtp:Debug = true in config or env
                var enableDebug = bool.TryParse(_config["Smtp:Debug"], out var dbg) && dbg;
                client = enableDebug
                    ? new MailKit.Net.Smtp.SmtpClient(new MailKit.ProtocolLogger("smtp.log"))
                    : new MailKit.Net.Smtp.SmtpClient();

                SecureSocketOptions options = SecureSocketOptions.Auto;
                if (port == 465)
                    options = SecureSocketOptions.SslOnConnect;
                else if (useStartTls || port == 587)
                    options = SecureSocketOptions.StartTls;

                _logger.LogDebug("SMTP attempt {Attempt}/{Max} connecting to {Host}:{Port}", attempt, maxAttempts, host, port);
                await client.ConnectAsync(host, port, options, cancellationToken);

                if (!string.IsNullOrEmpty(username))
                {
                    await client.AuthenticateAsync(username, password, cancellationToken);
                }

                await client.SendAsync(message, cancellationToken);
                await client.DisconnectAsync(true, cancellationToken);

                _logger.LogInformation("Sent email to {To} subject={Subject}", to, subject);

                // success - exit retry loop
                break;
            }
            catch (MailKit.Net.Smtp.SmtpCommandException ex)
            {
                // 4xx codes are transient per SMTP spec; allow retries
                _logger.LogWarning(ex, "SMTP command failed (attempt {Attempt}/{Max}) to send email to {To}: {StatusCode} {Message}", attempt, maxAttempts, to, ex.StatusCode, ex.Message);

                if (attempt == maxAttempts)
                {
                    _logger.LogError(ex, "Exhausted SMTP retries sending email to {To}", to);
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SMTP send failed (attempt {Attempt}/{Max}) to {To}", attempt, maxAttempts, to);
                if (attempt == maxAttempts)
                {
                    _logger.LogError(ex, "Exhausted SMTP retries sending email to {To}", to);
                    throw;
                }
            }
            finally
            {
                try { client?.Dispose(); } catch { }
            }

            // Exponential backoff with jitter
            var delaySeconds = Math.Pow(2, attempt);
            var jitter = rand.NextDouble() * 0.5;
            var delay = TimeSpan.FromSeconds(delaySeconds + jitter);
            _logger.LogInformation("Waiting {Delay} before next SMTP attempt", delay);
            await Task.Delay(delay, cancellationToken);
        }
    }
}
