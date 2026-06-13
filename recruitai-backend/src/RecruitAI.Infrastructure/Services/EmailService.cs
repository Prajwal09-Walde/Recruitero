using System;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RecruitAI.Application.Interfaces;

namespace RecruitAI.Infrastructure.Services;

public sealed class EmailService(IConfiguration configuration, ILogger<EmailService> logger) : IEmailService
{
    public async Task SendEmailAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        var host = configuration["Smtp:Host"];
        var username = configuration["Smtp:Username"];
        
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username))
        {
            logger.LogWarning(
                "\n============================================================\n" +
                "[SIMULATED EMAIL SERVICE - NO SMTP CONFIG]\n" +
                $"To: {to}\n" +
                $"Subject: {subject}\n\n" +
                $"{body}\n" +
                "============================================================"
            );
            return;
        }

        var portStr = configuration["Smtp:Port"] ?? "587";
        var port = int.Parse(portStr);
        var password = configuration["Smtp:Password"] ?? "";
        var enableSsl = bool.Parse(configuration["Smtp:EnableSsl"] ?? "true");
        var fromAddress = configuration["Smtp:FromAddress"] ?? "no-reply@recruitero.io";
        var fromName = configuration["Smtp:FromName"] ?? "Recruitero";

        try
        {
            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(username, password),
                EnableSsl = enableSsl
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(fromAddress, fromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = body.Contains("<html") || body.Contains("<div") || body.Contains("<p")
            };
            mailMessage.To.Add(to);

            // In .NET 8, SmtpClient SendMailAsync has an overload that accepts a CancellationToken
            await client.SendMailAsync(mailMessage, ct);
            logger.LogInformation("Successfully sent email to {To} via SMTP", to);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {To} via SMTP. Falling back to log print.", to);
            logger.LogWarning(
                "\n============================================================\n" +
                "[FALLBACK EMAIL SERVICE]\n" +
                $"To: {to}\n" +
                $"Subject: {subject}\n\n" +
                $"{body}\n" +
                "============================================================"
            );
        }
    }
}
