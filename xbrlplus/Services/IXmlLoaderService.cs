using System.Xml.Linq;

namespace xbrlplus.Services
{
    /// <summary>
    /// Loads XML from a given URI. Supports file and http/https schemes.
    /// </summary>
    public interface IXmlLoaderService
    {
        Task<XDocument> LoadAsync(Uri uri);
    }
}