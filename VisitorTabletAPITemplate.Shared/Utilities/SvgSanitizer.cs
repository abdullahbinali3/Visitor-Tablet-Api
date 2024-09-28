using AngleSharp.Dom;
using Ganss.Xss;

namespace VisitorTabletAPITemplate.Utilities
{
    public static class SvgSanitizer
    {
        private static HtmlSanitizer sanitizer = new HtmlSanitizer();
        private static List<string> _allowedImageSrcSchemes = new List<string>
        {
            "data:image/gif",
            "data:image/jpeg",
            "data:image/png",
            "data:image/jpg",
            "data:image/webp",
        };

        static SvgSanitizer()
        {
            // Remove default HtmlSanitizer tags, attributes and schemes
            sanitizer.AllowedTags.Clear();
            sanitizer.AllowedAttributes.Clear();
            sanitizer.AllowedSchemes.Clear();

            // Build list of allowed tags for svg
            // Taken from DOMPurify Javascript library: https://github.com/cure53/DOMPurify/blob/main/src/tags.js
            List<string> allowedTags = new List<string>
            {
                "svg",
                "a",
                "altglyph",
                "altglyphdef",
                "altglyphitem",
                "animatecolor",
                "animatemotion",
                "animatetransform",
                "circle",
                "clippath",
                "defs",
                "desc",
                "ellipse",
                "filter",
                "font",
                "g",
                "glyph",
                "glyphref",
                "hkern",
                //"image", // don't allow referenced raster images
                "line",
                "lineargradient",
                "marker",
                "mask",
                "metadata",
                "mpath",
                "path",
                "pattern",
                "polygon",
                "polyline",
                "radialgradient",
                "rect",
                "stop",
                "style",
                "switch",
                "symbol",
                "text",
                "textpath",
                "title",
                "tref",
                "tspan",
                "view",
                "vkern",
                // svg filters
                "feBlend",
                "feColorMatrix",
                "feComponentTransfer",
                "feComposite",
                "feConvolveMatrix",
                "feDiffuseLighting",
                "feDisplacementMap",
                "feDistantLight",
                "feFlood",
                "feFuncA",
                "feFuncB",
                "feFuncG",
                "feFuncR",
                "feGaussianBlur",
                "feImage",
                "feMerge",
                "feMergeNode",
                "feMorphology",
                "feOffset",
                "fePointLight",
                "feSpecularLighting",
                "feSpotLight",
                "feTile",
                "feTurbulence",
                // bad tags
                "use"
            };

            // Build list of allowed attributes for svg
            // Taken from DOMPurify Javascript library: https://github.com/cure53/DOMPurify/blob/main/src/attrs.js
            List<string> allowedAttributes = new List<string>
            {
                "accent-height",
                "accumulate",
                "additive",
                "alignment-baseline",
                "ascent",
                "attributename",
                "attributetype",
                "azimuth",
                "basefrequency",
                "baseline-shift",
                "begin",
                "bias",
                "by",
                "class",
                "clip",
                "clippathunits",
                "clip-path",
                "clip-rule",
                "color",
                "color-interpolation",
                "color-interpolation-filters",
                "color-profile",
                "color-rendering",
                "cx",
                "cy",
                "d",
                "dx",
                "dy",
                "diffuseconstant",
                "direction",
                "display",
                "divisor",
                "dur",
                "edgemode",
                "elevation",
                "end",
                "fill",
                "fill-opacity",
                "fill-rule",
                "filter",
                "filterunits",
                "flood-color",
                "flood-opacity",
                "font-family",
                "font-size",
                "font-size-adjust",
                "font-stretch",
                "font-style",
                "font-variant",
                "font-weight",
                "fx",
                "fy",
                "g1",
                "g2",
                "glyph-name",
                "glyphref",
                "gradientunits",
                "gradienttransform",
                "height",
                "href",
                "id",
                "image-rendering",
                "in",
                "in2",
                "k",
                "k1",
                "k2",
                "k3",
                "k4",
                "kerning",
                "keypoints",
                "keysplines",
                "keytimes",
                "lang",
                "lengthadjust",
                "letter-spacing",
                "kernelmatrix",
                "kernelunitlength",
                "lighting-color",
                "local",
                "marker-end",
                "marker-mid",
                "marker-start",
                "markerheight",
                "markerunits",
                "markerwidth",
                "maskcontentunits",
                "maskunits",
                "max",
                "mask",
                "media",
                "method",
                "mode",
                "min",
                "name",
                "numoctaves",
                "offset",
                "operator",
                "opacity",
                "order",
                "orient",
                "orientation",
                "origin",
                "overflow",
                "paint-order",
                "path",
                "pathlength",
                "patterncontentunits",
                "patterntransform",
                "patternunits",
                "points",
                "preservealpha",
                "preserveaspectratio",
                "primitiveunits",
                "r",
                "rx",
                "ry",
                "radius",
                "refx",
                "refy",
                "repeatcount",
                "repeatdur",
                "restart",
                "result",
                "rotate",
                "scale",
                "seed",
                "shape-rendering",
                "specularconstant",
                "specularexponent",
                "spreadmethod",
                "startoffset",
                "stddeviation",
                "stitchtiles",
                "stop-color",
                "stop-opacity",
                "stroke-dasharray",
                "stroke-dashoffset",
                "stroke-linecap",
                "stroke-linejoin",
                "stroke-miterlimit",
                "stroke-opacity",
                "stroke",
                "stroke-width",
                "style",
                "surfacescale",
                "systemlanguage",
                "tabindex",
                "targetx",
                "targety",
                "transform",
                "transform-origin",
                "text-anchor",
                "text-decoration",
                "text-rendering",
                "textlength",
                "type",
                "u1",
                "u2",
                "unicode",
                "values",
                "viewbox",
                "visibility",
                "version",
                "vert-adv-y",
                "vert-origin-x",
                "vert-origin-y",
                "width",
                "word-spacing",
                "wrap",
                "writing-mode",
                "xchannelselector",
                "ychannelselector",
                "x",
                "x1",
                "x2",
                "xmlns",
                "xmlns:xlink", // added by Shane
                "y",
                "y1",
                "y2",
                "z",
                "zoomandpan",
            };

            // Build list of allowed css properties for svg
            // Taken from: https://css-tricks.com/svg-properties-and-css/
            List<string> allowedCssProperties = new List<string>
            {
                // Text
                "alignment-baseline",
                "baseline-shift",
                "dominant-baseline",
                "glyph-orientation-horizontal",
                "glyph-orientation-vertical",
                "kerning",
                "text-anchor",
                // Clip
                "clip",
                "clip-path",
                "clip-rule",
                // Masking
                "mask",
                "opacity",
                // Filter effects
                "enable-background",
                "filter",
                "flood-color",
                "flood-opacity",
                "lighting-color",
                // Gradient
                "stop-color",
                "stop-opacity",
                // Interactivity
                //"pointer-events",
                // Color
                "color-profile",
                // Painting
                "color-interpolation",
                "color-interpolation-filters",
                "color-rendering",
                "fill",
                "fill-rule",
                "fill-opacity",
                "image-rendering",
                "marker",
                "marker-start",
                "marker-mid",
                "marker-end",
                "shape-rendering",
                "stroke",
                "stroke-dasharray",
                "stroke-dashoffset",
                "stroke-linecap",
                "stroke-linejoin",
                "stroke-miterlimit",
                "stroke-opacity",
                "stroke-width",
                "text-rendering",
                // SVG 2
                // Geometry
                "cx",
                "cy",
                "r",
                "rx",
                "ry",
                "height",
                "width",
                "x",
                "y",
                "path",
            };

            // Add allowed tags
            foreach (string tag in allowedTags)
            {
                sanitizer.AllowedTags.Add(tag);
            }

            // Add allowed attributes
            foreach (string attr in allowedAttributes)
            {
                sanitizer.AllowedAttributes.Add(attr);
            }

            // Add allowed css properties
            foreach (string cssProperty in allowedCssProperties)
            {
                sanitizer.AllowedCssProperties.Add(cssProperty);
            }

            // https://stackoverflow.com/questions/62573677/c-sharp-how-to-allow-embedded-image-htmlsanitizer/62598002#62598002
            sanitizer.AllowedTags.Remove("image");
            sanitizer.RemovingTag += (s, e) =>
            {
                switch (e.Tag.TagName.ToLowerInvariant())
                {
                    case "image":
                        {
                            // First assume the image tag is allowed
                            e.Cancel = true;

                            // Get all attributes of the tag similar to "href"
                            IAttr[] attributes = e.Tag.Attributes.Where(x => x.Name.Contains("href", StringComparison.OrdinalIgnoreCase)).ToArray();

                            if (attributes.Length == 0)
                            {
                                // If there are no href-like attributes, disallow the tag and remove it.
                                e.Cancel = false;
                            }
                            else
                            {
                                // If any of the href-like attributes' value do not start with the allowed schemes, then disallow the tag and remove it.
                                foreach (IAttr attribute in attributes)
                                {
                                    if (!_allowedImageSrcSchemes.Exists(x => attribute.Value.StartsWith(x)))
                                    {
                                        //e.Reason = RemoveReason.NotAllowedAttribute;
                                        e.Cancel = false;
                                        break;
                                    }
                                }
                            }

                            break;
                        }
                }
            };

            sanitizer.RemovingAttribute += (s, e) =>
            {
                switch (e.Tag.TagName.ToLowerInvariant())
                {
                    case "image":
                        {
                            if (e.Attribute.Name == "href" && _allowedImageSrcSchemes.Exists(x => e.Attribute.Value.StartsWith(x)))
                            {
                                //e.Reason = RemoveReason.NotAllowedAttribute;
                                e.Cancel = true;
                            }

                            break;
                        }
                }
            };
        }

        public static string Sanitize(string svgContent)
        {
            return sanitizer.Sanitize(svgContent);
        }
    }
}
