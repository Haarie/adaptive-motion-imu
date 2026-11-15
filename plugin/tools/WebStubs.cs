// Compile-only stand-in when Mono has no System.Web.Extensions (update checks are not run off-Windows).
namespace System.Web.Script.Serialization
{
    public class JavaScriptSerializer
    {
        public int MaxJsonLength { get; set; }
        public T Deserialize<T>(string input) { throw new NotSupportedException("stub"); }
    }
}
