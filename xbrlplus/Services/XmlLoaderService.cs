using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace xbrlplus.Services
{
	/// <summary>
	/// Loads XML from file or HTTP/HTTPS URI, with caching for web resources.
	/// </summary>
	public class XmlLoaderService : IXmlLoaderService
	{
		private static readonly HttpClient _httpClient = new();
		private readonly string _cacheDir;
		private readonly TimeSpan _cacheLifetime = TimeSpan.FromDays(7); // Cache valid for 7 days

		public XmlLoaderService()
		{
			var userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			_cacheDir = Path.Combine(userDir, ".xbrlplus", "cache");
			Directory.CreateDirectory(_cacheDir);
		}

		public async Task<XDocument> LoadAsync(Uri uri)
		{
			switch (uri.Scheme.ToLowerInvariant())
			{
				case "file":
					return XDocument.Load(uri.LocalPath);

				case "http":
				case "https":
					var cachePath = GetCachePath(uri);
					if (File.Exists(cachePath))
					{
						// Check cache expiration
						var lastWrite = File.GetLastWriteTimeUtc(cachePath);
						if (DateTime.UtcNow - lastWrite < _cacheLifetime)
						{
							// Load from cache
							return XDocument.Load(cachePath);
						}
					}
					// Download and cache
					var xmlContent = await _httpClient.GetStringAsync(uri);
					await File.WriteAllTextAsync(cachePath, xmlContent);
					return XDocument.Parse(xmlContent);

				default:
					throw new UnsupportedSchemeException($"Unsupported URI scheme: {uri.Scheme}");
			}
		}

		private string GetCachePath(Uri uri)
		{
			// Use a hash of the URI for cache filename
			var hash = GetStableHash(uri);
			var ext = Path.GetExtension(uri.AbsolutePath);
			return Path.Combine(_cacheDir, $"{hash}{ext}");
		}

		private static string GetStableHash(Uri uri)
		{
			using var sha = SHA256.Create();
			var bytes = Encoding.UTF8.GetBytes(uri.AbsoluteUri);
			var hash = sha.ComputeHash(bytes);
			return Convert.ToHexString(hash);
		}
	}

	/// <summary>
	/// Exception for unsupported URI schemes.
	/// </summary>
	public class UnsupportedSchemeException : Exception
	{
		public UnsupportedSchemeException(string message) : base(message) { }
	}
}