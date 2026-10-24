using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using CleanSlate.Core.Interfaces;

namespace CleanSlate.Windows;

/// <summary>
/// Trusts the embedded self-signed code-signing certificate. Per-user stores are used (no administrator rights); when the process
/// is elevated the machine stores are used as well. Windows itself asks the user to confirm adding a root certificate.
/// </summary>
public sealed class WindowsCertificateTrust : ICertificateTrust
{
    public const string ResourceName = "CleanSlate.SigningCertificate.cer";

    private readonly IPrivilegeInfo _privilege;
    private readonly Lazy<X509Certificate2?> _certificate;

    public WindowsCertificateTrust(IPrivilegeInfo privilege, Assembly resourceAssembly, string? executablePath)
    {
        _privilege = privilege;
        _certificate = new Lazy<X509Certificate2?>(() => Load(resourceAssembly, executablePath));
    }

    public bool IsAvailable => _certificate.Value is not null;

    public bool IsTrusted => _certificate.Value is { } cert && (Contains(StoreLocation.CurrentUser, cert) || Contains(StoreLocation.LocalMachine, cert));

    public string Description => _certificate.Value is { } cert
        ? $"{cert.GetNameInfo(X509NameType.SimpleName, false)}, valid until {cert.NotAfter:yyyy-MM-dd}"
        : "No signing certificate is embedded in this copy, or this copy is not signed with it.";

    public void Trust()
    {
        var cert = _certificate.Value ?? throw new InvalidOperationException("This copy is not signed with the embedded certificate.");
        Add(StoreName.Root, StoreLocation.CurrentUser, cert);
        Add(StoreName.TrustedPublisher, StoreLocation.CurrentUser, cert);
        if (_privilege.IsElevated)
        {
            Add(StoreName.Root, StoreLocation.LocalMachine, cert);
            Add(StoreName.TrustedPublisher, StoreLocation.LocalMachine, cert);
        }
    }

    public void Remove()
    {
        var cert = _certificate.Value;
        if (cert is null)
        {
            return;
        }

        foreach (var name in new[] { StoreName.Root, StoreName.TrustedPublisher })
        {
            Delete(name, StoreLocation.CurrentUser, cert);
            if (_privilege.IsElevated)
            {
                Delete(name, StoreLocation.LocalMachine, cert);
            }
        }
    }

    private static X509Certificate2? Load(Assembly assembly, string? executablePath)
    {
        try
        {
            using var stream = assembly.GetManifestResourceStream(ResourceName);
            if (stream is null || string.IsNullOrEmpty(executablePath))
            {
                return null;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var embedded = X509CertificateLoader.LoadCertificate(buffer.ToArray());

            using var signer = SignerOf(executablePath);
            return signer is not null && string.Equals(signer.Thumbprint, embedded.Thumbprint, StringComparison.OrdinalIgnoreCase)
                ? embedded
                : null;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

#pragma warning disable SYSLIB0057
    private static X509Certificate2? SignerOf(string path)
    {
        try
        {
            using var signer = X509Certificate.CreateFromSignedFile(path);
            return new X509Certificate2(signer);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
#pragma warning restore SYSLIB0057

    private static bool Contains(StoreLocation location, X509Certificate2 cert)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, location);
            store.Open(OpenFlags.ReadOnly);
            return store.Certificates.Find(X509FindType.FindByThumbprint, cert.Thumbprint, validOnly: false).Count > 0;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    private static void Add(StoreName name, StoreLocation location, X509Certificate2 cert)
    {
        using var store = new X509Store(name, location);
        store.Open(OpenFlags.ReadWrite);
        store.Add(cert);
    }

    private static void Delete(StoreName name, StoreLocation location, X509Certificate2 cert)
    {
        try
        {
            using var store = new X509Store(name, location);
            store.Open(OpenFlags.ReadWrite);
            store.Remove(cert);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Not present, or the user declined Windows' confirmation.
        }
    }
}
