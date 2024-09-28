namespace VisitorTabletAPITemplate.FileStorage
{
    public static class FileStorageHelpers
    {
        // Attachment files
        public const string InvalidAttachmentFileTypes = ".ade, .adp, .apk, .appx, .appxbundle, .bat, .cab, .chm, .cmd, .com, .cpl, .diagcab, .diagcfg, .diagpack, .dll, .dmg, .ex, .ex_, .exe, .hta, .img, .ins, .iso, .isp, .jar, .jnlp, .js, .jse, .lib, .lnk, .mde, .msc, .msi, .msix, .msixbundle, .msp, .mst, .nsh, .pif, .ps1, .scr, .sct, .shb, .sys, .vb, .vbe, .vbs, .vhd, .vxd, .wsc, .wsf, .wsh, .xll";

        public static bool IsValidAttachmentExtension(string extension)
        {
            switch (extension)
            {
                // https://support.google.com/mail/answer/6590?hl=en#zippy=%2Cmessages-that-have-attachments
                //.ade, .adp, .apk, .appx, .appxbundle, .bat, .cab, .chm, .cmd, .com, .cpl, .diagcab, .diagcfg, .diagpack, .dll, .dmg, .ex, .ex_, .exe, .hta, .img, .ins, .iso, .isp, .jar, .jnlp, .js, .jse, .lib, .lnk, .mde, .msc, .msi, .msix, .msixbundle, .msp, .mst, .nsh, .pif, .ps1, .scr, .sct, .shb, .sys, .vb, .vbe, .vbs, .vhd, .vxd, .wsc, .wsf, .wsh, .xll

                // Without dot
                case "ade":
                case "adp":
                case "apk":
                case "appx":
                case "appxbundle":
                case "bat":
                case "cab":
                case "chm":
                case "cmd":
                case "com":
                case "cpl":
                case "diagcab":
                case "diagcfg":
                case "diagpack":
                case "dll":
                case "dmg":
                case "ex":
                case "ex_":
                case "exe":
                case "hta":
                case "img":
                case "ins":
                case "iso":
                case "isp":
                case "jar":
                case "jnlp":
                case "js":
                case "jse":
                case "lib":
                case "lnk":
                case "mde":
                case "msc":
                case "msi":
                case "msix":
                case "msixbundle":
                case "msp":
                case "mst":
                case "nsh":
                case "pif":
                case "ps1":
                case "scr":
                case "sct":
                case "shb":
                case "sys":
                case "vb":
                case "vbe":
                case "vbs":
                case "vhd":
                case "vxd":
                case "wsc":
                case "wsf":
                case "wsh":
                case "xll":
                // With dot
                case ".ade":
                case ".adp":
                case ".apk":
                case ".appx":
                case ".appxbundle":
                case ".bat":
                case ".cab":
                case ".chm":
                case ".cmd":
                case ".com":
                case ".cpl":
                case ".diagcab":
                case ".diagcfg":
                case ".diagpack":
                case ".dll":
                case ".dmg":
                case ".ex":
                case ".ex_":
                case ".exe":
                case ".hta":
                case ".img":
                case ".ins":
                case ".iso":
                case ".isp":
                case ".jar":
                case ".jnlp":
                case ".js":
                case ".jse":
                case ".lib":
                case ".lnk":
                case ".mde":
                case ".msc":
                case ".msi":
                case ".msix":
                case ".msixbundle":
                case ".msp":
                case ".mst":
                case ".nsh":
                case ".pif":
                case ".ps1":
                case ".scr":
                case ".sct":
                case ".shb":
                case ".sys":
                case ".vb":
                case ".vbe":
                case ".vbs":
                case ".vhd":
                case ".vxd":
                case ".wsc":
                case ".wsf":
                case ".wsh":
                case ".xll":
                    return false;
            }

            return true;
        }

        public static bool IsValidAttachmentExtensionFromFilename(string filename)
        {
            string? extension = Path.GetExtension(filename?.ToLowerInvariant()); // includes dot (.)

            if (extension is null)
            {
                return false;
            }

            return IsValidAttachmentExtension(extension);
        }

        // PDF Files
        public const string ValidPdfFileTypes = ".pdf";

        public static bool IsValidPdfExtension(string extension)
        {
            switch (extension)
            {
                case "pdf":
                case ".pdf":
                    return true;
            }

            return true;
        }

        public static bool IsValidPdfExtensionFromFilename(string filename)
        {
            string? extension = Path.GetExtension(filename?.ToLowerInvariant()); // includes dot (.)

            if (extension is null)
            {
                return false;
            }

            return IsValidPdfExtension(extension);
        }

        // Video files
        public const string ValidVideoFileTypes = ".mp4, .webm, .ogv";

        public static bool IsValidVideoExtension(string extension)
        {
            switch (extension)
            {
                case "mp4":
                case "webm":
                case "ogv":
                case ".mp4":
                case ".webm":
                case ".ogv":
                    return true;
            }

            return true;
        }

        public static bool IsValidVideoExtensionFromFilename(string filename)
        {
            string? extension = Path.GetExtension(filename?.ToLowerInvariant()); // includes dot (.)

            if (extension is null)
            {
                return false;
            }

            return IsValidVideoExtension(extension);
        }

        // Excel Files
        public const string ValidExcelFileTypes = ".xlsx";

        public static bool IsValidExcelExtension(string extension)
        {
            switch (extension)
            {
                case "xlsx":
                case ".xlsx":
                    return true;
            }

            return true;
        }

        public static bool IsValidExcelExtensionFromFilename(string filename)
        {
            string? extension = Path.GetExtension(filename?.ToLowerInvariant()); // includes dot (.)

            if (extension is null)
            {
                return false;
            }

            return IsValidExcelExtension(extension);
        }
    }
}
