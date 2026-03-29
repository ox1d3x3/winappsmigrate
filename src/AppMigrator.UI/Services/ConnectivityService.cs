using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace AppMigrator.UI.Services;

public sealed class ConnectivitySnapshot
{
    public bool IsNetworkAvailable { get; init; }
    public bool HasInternetAccess { get; init; }
    public bool InternetProbeVerified { get; init; }
    public string Summary { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed class ConnectivityService
{
    private static readonly Uri[] ProbeUris =
    {
        new("https://www.msftconnecttest.com/connecttest.txt"),
        new("https://www.cloudflare.com/cdn-cgi/trace"),
        new("https://www.google.com/generate_204")
    };

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3)
    };

    public async Task<ConnectivitySnapshot> GetStatusAsync(bool includeInternetProbe = false, CancellationToken cancellationToken = default)
    {
        var hasNetwork = NetworkInterface.GetIsNetworkAvailable()
            && NetworkInterface.GetAllNetworkInterfaces().Any(ni =>
                ni.OperationalStatus == OperationalStatus.Up
                && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel);

        if (!hasNetwork)
        {
            return new ConnectivitySnapshot
            {
                IsNetworkAvailable = false,
                HasInternetAccess = false,
                InternetProbeVerified = false,
                Summary = "Disconnected",
                Detail = "No active network adapter detected."
            };
        }

        if (!includeInternetProbe)
        {
            return new ConnectivitySnapshot
            {
                IsNetworkAvailable = true,
                HasInternetAccess = true,
                InternetProbeVerified = false,
                Summary = "Connected",
                Detail = "Active network adapter detected."
            };
        }

        var probe = await ProbeInternetAsync(cancellationToken).ConfigureAwait(false);
        return new ConnectivitySnapshot
        {
            IsNetworkAvailable = true,
            HasInternetAccess = probe.Verified,
            InternetProbeVerified = probe.Verified,
            Summary = probe.Verified ? "Connected" : "Connected",
            Detail = probe.Detail
        };
    }

    private static async Task<(bool Verified, string Detail)> ProbeInternetAsync(CancellationToken cancellationToken)
    {
        foreach (var uri in ProbeUris)
        {
            if (await ProbeUrlAsync(uri, cancellationToken).ConfigureAwait(false))
            {
                return (true, $"Internet connection verified via {uri.Host}.");
            }
        }

        if (await ProbeDnsAsync("github.com", cancellationToken).ConfigureAwait(false)
            || await ProbeDnsAsync("www.microsoft.com", cancellationToken).ConfigureAwait(false))
        {
            return (false, "Active network adapter detected and DNS is responding, but direct internet verification did not complete. You can retry or continue.");
        }

        return (false, "Active network adapter detected, but internet could not be verified. You can retry or continue.");
    }

    private static async Task<bool> ProbeUrlAsync(Uri uri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ProbeDnsAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            return addresses.Any(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork || address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);
        }
        catch
        {
            return false;
        }
    }
}
