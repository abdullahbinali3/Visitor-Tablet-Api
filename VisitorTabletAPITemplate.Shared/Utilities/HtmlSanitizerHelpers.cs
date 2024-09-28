using Ganss.Xss;

namespace VisitorTabletAPITemplate.Utilities
{
    public static class HtmlSanitizerHelpers
    {
        private static HtmlSanitizer sanitizer = new HtmlSanitizer();
        private static List<string> _allowedSrcSchemes = new List<string>
        {
            "data:image/gif",
            "data:image/jpeg",
            "data:image/png",
            "data:image/jpg",
            "data:image/webp",
            "http://",
            "https://",
            "mailto:",
            "cid:"
        };

        static HtmlSanitizerHelpers()
        {
            // allow class attribute
            sanitizer.AllowedAttributes.Add("class");

            /*
            // http, https are allowed by default, allow mailto, data and cid as well
            sanitizer.AllowedSchemes.Add("mailto");
            sanitizer.AllowedSchemes.Add("data");
            sanitizer.AllowedSchemes.Add("cid");
            */

            // https://stackoverflow.com/questions/62573677/c-sharp-how-to-allow-embedded-image-htmlsanitizer/62598002#62598002
            sanitizer.AllowedAttributes.Remove("src");
            sanitizer.RemovingAttribute += (s, e) =>
            {
                switch (e.Tag.TagName.ToLowerInvariant())
                {
                    case "img":
                        {
                            if (e.Attribute.Name == "src" && _allowedSrcSchemes.Exists(x => e.Attribute.Value.StartsWith(x)))
                            {
                                //e.Reason = RemoveReason.NotAllowedAttribute;
                                e.Cancel = true;
                            }

                            break;
                        }
                }
            };
        }

        public static string SanitizeHtml(string htmlContent)
        {
            return sanitizer.Sanitize(htmlContent);
        }
    }
}
