using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using VibeDeck.Host.Connect;
using VibeDeck.Host.Security;

namespace VibeDeck.Host
{
    public partial class Startup
    {
        // Read-only onboarding assets phones fetch during pairing/HTTPS setup:
        // the pairing QR image and the downloadable local-CA / host certificates.
        // The shared writers WriteQrSvgAsync/BuildQrSvg/WriteCertificateFileAsync
        // remain on the Startup partial class.
        private static void MapOnboardingAssetEndpoints(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGet("/qr.svg", async context =>
            {
                var provider = context.RequestServices.GetRequiredService<ConnectInfoProvider>();
                var connectInfo = provider.Get(context);
                var phonePageUrl = new Uri(new Uri(connectInfo.PreferredUrl), "index.html").ToString();
                await WriteQrSvgAsync(context, phonePageUrl);
            });

            endpoints.MapGet("/cert/vibedeck-root.cer", async context =>
            {
                await WriteCertificateFileAsync(
                    context,
                    LocalHttpsCertificate.RootCertificatePath,
                    "vibedeck-root.cer",
                    "application/x-x509-ca-cert");
            });

            endpoints.MapGet("/cert/vibedeck-root.crt", async context =>
            {
                await WriteCertificateFileAsync(
                    context,
                    LocalHttpsCertificate.RootCertificatePath,
                    "vibedeck-root.crt",
                    "application/x-x509-ca-cert");
            });

            endpoints.MapGet("/cert/vibedeck-host.cer", async context =>
            {
                await WriteCertificateFileAsync(
                    context,
                    LocalHttpsCertificate.HostCertificatePath,
                    "vibedeck-host.cer",
                    "application/x-x509-ca-cert");
            });

        }
    }
}
