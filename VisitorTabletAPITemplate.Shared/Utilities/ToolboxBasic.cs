using Dapper;
using EmailValidation;
using Microsoft.Data.SqlClient;
using System.Buffers.Text;
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VisitorTabletAPITemplate.Utilities
{
    public static partial class Toolbox
    {
        static JsonSerializerOptions _jsonSerializerOptionsWriteIndented = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        public static JsonSerializerOptions JsonSerializerOptionsWriteIndented { get { return _jsonSerializerOptionsWriteIndented; } }
        static JsonSerializerOptions _jsonSerializerOptionsCamelCase = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        public static JsonSerializerOptions JsonSerializerOptionsCamelCase { get { return _jsonSerializerOptionsCamelCase; } }
        public static readonly char[] EmailsSeparator = new char[] { ',', ';' };

        /// <summary>
        /// <para>Returns a formatted string containing details for a single Exception, intended to be used for writing to a log or sending an error email notification.</para>
        /// <para>This does not include InnerExceptions. For all InnerExceptions please use <see cref="GetExceptionString(Exception)"/> instead.</para>
        /// <para>For WebExceptions, it will also retrieve and include the web response content. For SqlExceptions, for each SQL error it will also include query line numbers and details from the database.</para>
        /// </summary>
        /// <param name="exception">The Exception to parse details for.</param>
        /// <returns></returns>
        public static string GetExceptionStringSingle(Exception exception)
        {
            StringBuilder errorStr = new StringBuilder();

            string newLine = Environment.NewLine;
            string twoNewLines = Environment.NewLine + Environment.NewLine;

            errorStr.Append(exception.Source);
            errorStr.Append(twoNewLines);
            errorStr.Append(exception.GetType().ToString() + ": " + exception.Message);
            errorStr.Append(twoNewLines);
            errorStr.Append(exception.StackTrace);

            string webResponse = string.Empty;

            // If Exception is a WebException, get the response data
            // and display it on the email.
            if (exception.GetType() == typeof(WebException))
            {
                WebException webEx = (WebException)exception;

                if (webEx.Response != null)
                {
                    using (Stream responseStream = webEx.Response.GetResponseStream())
                    {
                        if (responseStream != null)
                        {
                            try
                            {
                                webResponse = new StreamReader(webEx.Response.GetResponseStream()).ReadToEnd();
                            }
                            catch (Exception e)
                            {
                                webResponse = "*** Exception occurred while reading response content:" + twoNewLines + GetExceptionString(e);
                            }

                            if (!string.IsNullOrEmpty(webResponse))
                            {
                                errorStr.Append(twoNewLines + "Web Response content:" + twoNewLines);
                                errorStr.Append(webResponse + twoNewLines);
                            }
                        }
                    }
                }

                if (webResponse == "")
                {
                    errorStr.Append(twoNewLines + "There was no content in the body of the Web Response." + twoNewLines);
                }
            }
            // If Exception is a SqlException, get additional info.
            else if (exception.GetType() == typeof(SqlException))
            {
                SqlException sqlEx = (SqlException)exception;
                errorStr.Append(twoNewLines + "SQL Exception details:");

                if (sqlEx.Errors.Count == 0)
                {
                    errorStr.Append(twoNewLines + "  No additional information.");
                }
                else
                {
                    for (int i = 0; i < sqlEx.Errors.Count; i++)
                    {
                        errorStr.Append(twoNewLines + "  SQL Error #" + (i + 1) + " of " + sqlEx.Errors.Count + newLine +
                            "  Message: " + sqlEx.Errors[i].Message + newLine +
                            "  Error Number: " + sqlEx.Errors[i].Number + newLine +
                            "  Line Number: " + sqlEx.Errors[i].LineNumber + newLine +
                            "  Source: " + sqlEx.Errors[i].Source + newLine +
                            "  Procedure: " + sqlEx.Errors[i].Procedure);
                    }
                }
            }
            // If Exception is a DbException or derived from DbException, get additional info.
            else if (exception.GetType() == typeof(DbException) || exception.GetType().IsSubclassOf(typeof(DbException)))
            {
                DbException dbEx = (DbException)exception;
                errorStr.Append(twoNewLines + "DB Exception details:");

                errorStr.AppendLine(twoNewLines + "  Base Error Code: " + dbEx.ErrorCode);

                foreach (DictionaryEntry entry in dbEx.Data)
                {
                    errorStr.AppendLine("  " + entry.Key + ": " + entry.Value);
                }
            }

            return errorStr.ToString();
        }

        /// <summary>
        /// <para>Returns a formatted string containing details for an Exception and all its InnerExceptions, intended to be used for writing to a log or sending an error email notification.</para>
        /// <para>For WebExceptions, it will also retrieve the web response content. For SqlExceptions, for each SQL error it will also include query line numbers and details from the database.</para>
        /// </summary>
        /// <param name="exception">The Exception to parse details for.</param>
        /// <returns></returns>
        public static string GetExceptionString(Exception exception)
        {
            StringBuilder errorStr = new StringBuilder();

            string twoNewLines = Environment.NewLine + Environment.NewLine;

            // Parse exception details
            errorStr.Append(GetExceptionStringSingle(exception));

            // Loop through all inner exceptions
            Exception? inner = exception.InnerException;

            while (inner != null)
            {
                errorStr.Append(twoNewLines + "InnerException:" + twoNewLines);

                // Parse inner exception details
                errorStr.Append(GetExceptionStringSingle(inner));

                // Get next inner exception
                inner = inner.InnerException;
            }

            // Return full message
            return errorStr.ToString();
        }

        /// <summary>
        /// Sends an error email.
        /// </summary>
        /// <param name="error">The error message.</param>
        /// <param name="applicationName">Name of the application with the error. This will appear in the subject and body of the email.</param>
        /// <param name="host">The host address of the mail server to use for sending the email.</param>
        /// <param name="port">The port of the mail server to use for sending the email.</param>
        /// <param name="to">List of email addresses for the To field of the email.</param>
        /// <param name="cc">List of email addresses for the CC field of the email.</param>
        /// <param name="bcc">List of email addresses for the BCC field of the email.</param>
        /// <param name="from">From address of the email.</param>
        /// <param name="bodyBegin">String to add to the beginning of the email body.</param>
        /// <param name="bodyEnd">String to add to the end of the email body after the error details.</param>
        public static void SendErrorEmail(string error, string applicationName, string host, int port, MailAddressCollection? to, MailAddressCollection? cc, MailAddressCollection? bcc,
            MailAddress from, string? bodyBegin = null, string? bodyEnd = null)
        {
            // Set up SmtpClient
            SmtpClient client = new SmtpClient();
            client.Port = port;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.UseDefaultCredentials = true;
            client.Host = host;

            // Set up MailMessage
            MailMessage mail = new MailMessage();

            // Add email addresses
            if (to != null) { foreach (MailAddress email in to) { mail.To.Add(email); } }
            if (cc != null) { foreach (MailAddress email in cc) { mail.CC.Add(email); } }
            if (bcc != null) { foreach (MailAddress email in bcc) { mail.Bcc.Add(email); } }

            mail.From = from;

            // Get subject and body for error email.
            mail.Subject = GetErrorEmailSubject(applicationName);
            mail.Body = GetErrorEmailBody(error, applicationName, bodyBegin, bodyEnd);

            client.Send(mail);
        }

        /// <summary>
        /// Sends an error email.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <param name="applicationName">Name of the application with the error. This will appear in the subject and body of the email.</param>
        /// <param name="host">The host address of the mail server to use for sending the email.</param>
        /// <param name="port">The port of the mail server to use for sending the email.</param>
        /// <param name="to">List of email addresses for the To field of the email.</param>
        /// <param name="from">From address of the email.</param>
        /// <param name="bodyBegin">String to add to the beginning of the email body.</param>
        /// <param name="bodyEnd">String to add to the end of the email body after the exception details.</param>
        public static void SendErrorEmail(Exception exception, string applicationName, string host, int port, string to, string from, string? bodyBegin = null, string? bodyEnd = null)
        {
            // Add email addresses into a MailAddressCollection.
            MailAddressCollection toMac = StringToMailAddressCollection(to);

            // Send the email.
            SendErrorEmail(exception, applicationName, host, port, toMac, null, null, new MailAddress(from), bodyBegin, bodyEnd);
        }

        /// <summary>
        /// Sends an error email.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <param name="applicationName">Name of the application with the error. This will appear in the subject and body of the email.</param>
        /// <param name="host">The host address of the mail server to use for sending the email.</param>
        /// <param name="port">The port of the mail server to use for sending the email.</param>
        /// <param name="to">List of email addresses for the To field of the email.</param>
        /// <param name="cc">List of email addresses for the CC field of the email.</param>
        /// <param name="bcc">List of email addresses for the BCC field of the email.</param>
        /// <param name="from">From address of the email.</param>
        /// <param name="bodyBegin">String to add to the beginning of the email body.</param>
        /// <param name="bodyEnd">String to add to the end of the email body after the exception details.</param>
        public static void SendErrorEmail(Exception exception, string applicationName, string host, int port, MailAddressCollection? to, MailAddressCollection? cc, MailAddressCollection? bcc,
            MailAddress from, string? bodyBegin = null, string? bodyEnd = null)
        {
            SendErrorEmail(GetExceptionString(exception), applicationName, host, port, to, cc, bcc, from, bodyBegin, bodyEnd);
        }

        /// <summary>
        /// Sends an error email.
        /// </summary>
        /// <param name="error">The error message.</param>
        /// <param name="applicationName">Name of the application with the error. This will appear in the subject and body of the email.</param>
        /// <param name="host">The host address of the mail server to use for sending the email.</param>
        /// <param name="port">The port of the mail server to use for sending the email.</param>
        /// <param name="to">List of email addresses for the To field of the email, separated by comma or semicolon.</param>
        /// <param name="from">From address of the email.</param>
        /// <param name="bodyBegin">String to add to the beginning of the email body.</param>
        /// <param name="bodyEnd">String to add to the end of the email body after the error details.</param>
        public static void SendErrorEmail(string error, string applicationName, string host, int port, string to, string from, string? bodyBegin = null, string? bodyEnd = null)
        {
            // Add email addresses into a MailAddressCollection.
            MailAddressCollection toMac = StringToMailAddressCollection(to);

            // Send the email.
            SendErrorEmail(error, applicationName, host, port, toMac, null, null, new MailAddress(from), bodyBegin, bodyEnd);
        }

        /// <summary>
        /// <para>Takes a string containing email recipients separated by commas or semicolons.</para>
        /// <para>Whitespace will be removed from the string before processing.</para>
        /// <para>Returns an <see cref="SortedSet{string}"/> containing a unique list of recipients which preserves insertion order.</para>
        /// </summary>
        /// <param name="emails">String containing email recipients separated by commas or semicolons.</param>
        /// <returns></returns>
        public static SortedSet<string> SplitRecipientsString(string emails)
        {
            if (string.IsNullOrWhiteSpace(emails))
                return new SortedSet<string>();

            // Remove whitespace
            emails = GeneratedRegexes.ContainsWhitespace().Replace(emails, "");

            // Split emails into an array, separating by comma or semicolon.
            string[] emailsArr = emails.Split(EmailsSeparator, StringSplitOptions.RemoveEmptyEntries);

            // Return ordered set (uniquifies)
            return new SortedSet<string>(emailsArr);
        }

        /// <summary>
        /// Takes a string of email addresses and returns a <see cref="MailAddressCollection"/>. Mail address list will be split on both comma and semicolons.
        /// </summary>
        /// <param name="addresses">A list of email addresses separated by commas or semicolons.</param>
        /// <returns></returns>
        public static MailAddressCollection StringToMailAddressCollection(string addresses)
        {
            MailAddressCollection mac = new MailAddressCollection();

            SortedSet<string> uniqueList = SplitRecipientsString(addresses);

            foreach (string address in uniqueList)
            {
                mac.Add(new MailAddress(address));
            }

            return mac;
        }

        private static string GetErrorEmailSubject(string applicationName)
        {
            return applicationName + ": An Error Occurred!";
        }

        private static string GetErrorEmailBody(string error, string applicationName, string? bodyBegin = null, string? bodyEnd = null)
        {
            string result = "An error has occurred in " + applicationName + ":\n\n\n\n";

            if (!string.IsNullOrEmpty(bodyBegin))
                result += bodyBegin + "\n\n\n\n";

            result += "Error details:\n\n" + error;

            if (!string.IsNullOrEmpty(bodyEnd))
                result += "\n\n\n\n" + bodyEnd;

            return result;
        }

        /// <summary>
        /// Sends an email.
        /// </summary>
        /// <param name="host">The host address of the mail server to use for sending the email.</param>
        /// <param name="port">The port of the mail server to use for sending the email.</param>
        /// <param name="to">List of email addresses for the To field of the email, separated by comma or semicolon.</param>
        /// <param name="from">From address of the email.</param>
        /// <param name="subject">Subject line of the email.</param>
        /// <param name="body">Body of the email.</param>
        /// <param name="isBodyHTML">Whether the body is in HTML format or not.</param>
        /// <param name="attachments">An enumerable of <see cref="MailAttachmentData"/> containing attachment(s) data.</param>
        public static void SendEmail(string host, int port, string to, string from, string? subject, string? body, bool isBodyHTML,
            IEnumerable<MailAttachmentData>? attachments)
        {
            // Add email addresses into a MailAddressCollection.
            MailAddressCollection toMac = StringToMailAddressCollection(to);

            // Send the email.
            SendEmail(host, port, toMac, null, null, new MailAddress(from), subject, body, isBodyHTML, attachments);
        }

        /// <summary>
        /// Sends an email.
        /// </summary>
        /// <param name="host">The host address of the mail server to use for sending the email.</param>
        /// <param name="port">The port of the mail server to use for sending the email.</param>
        /// <param name="to">List of email addresses for the To field of the email.</param>
        /// <param name="cc">List of email addresses for the CC field of the email.</param>
        /// <param name="bcc">List of email addresses for the BCC field of the email.</param>
        /// <param name="from">From address of the email.</param>
        /// <param name="subject">Subject line of the email.</param>
        /// <param name="body">Body of the email.</param>
        /// <param name="isBodyHTML">Whether the body is in HTML format or not.</param>
        /// <param name="attachments">An enumerable of <see cref="MailAttachmentData"/> containing attachment(s) data.</param>
        public static void SendEmail(string host, int port, MailAddressCollection? to, MailAddressCollection? cc, MailAddressCollection? bcc,
            MailAddress from, string? subject, string? body, bool isBodyHTML, IEnumerable<MailAttachmentData>? attachments)
        {
            // Set up SmtpClient
            SmtpClient client = new SmtpClient();
            client.Port = port;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.UseDefaultCredentials = true;
            client.Host = host;

            // Set up MailMessage
            MailMessage mail = new MailMessage();

            // Add email addresses
            if (to != null) { foreach (MailAddress email in to) { mail.To.Add(email); } }
            if (cc != null) { foreach (MailAddress email in cc) { mail.CC.Add(email); } }
            if (bcc != null) { foreach (MailAddress email in bcc) { mail.Bcc.Add(email); } }

            // Set email fields
            mail.From = from;
            mail.Subject = subject;
            mail.Body = body;
            mail.IsBodyHtml = isBodyHTML;

            if (attachments != null && attachments.Any())
            {
                // Build the attachment streams and add the attachments to the email
                List<MemoryStream> streams = new List<MemoryStream>();

                foreach (MailAttachmentData attachment in attachments)
                {
                    byte[] dataBytes = Convert.FromBase64String(attachment.DataBase64 ?? "");

                    MemoryStream attachmentStream = new MemoryStream(dataBytes);

                    mail.Attachments.Add(new Attachment(attachmentStream, attachment.FileName, attachment.MimeType));

                    streams.Add(attachmentStream);
                }

                // Send the email
                client.Send(mail);

                // Dispose of the streams
                foreach (MemoryStream ms in streams)
                {
                    ms.Dispose();
                }
            }
            else
            {
                // If no attachments just send the email
                client.Send(mail);
            }
        }

        /// <summary>
        /// Sends an email.
        /// </summary>
        /// <param name="host">The host address of the mail server to use for sending the email.</param>
        /// <param name="port">The port of the mail server to use for sending the email.</param>
        /// <param name="username">The username mail server to use for sending the email.</param>
        /// <param name="password">The password of the mail server to use for sending the email.</param>
        /// <param name="to">List of email addresses for the To field of the email.</param>
        /// <param name="cc">List of email addresses for the CC field of the email.</param>
        /// <param name="bcc">List of email addresses for the BCC field of the email.</param>
        /// <param name="from">From address of the email.</param>
        /// <param name="subject">Subject line of the email.</param>
        /// <param name="body">Body of the email.</param>
        /// <param name="isBodyHTML">Whether the body is in HTML format or not.</param>
        /// <param name="attachments">An enumerable of <see cref="MailAttachmentData"/> containing attachment(s) data.</param>
        public static void SendEmail(string host, int port, string? username, string? password, string? messageIdHeader, string? inReplyToIdHeader, MailAddressCollection? to, MailAddressCollection? cc, MailAddressCollection? bcc,
            MailAddress from, string? subject, string? body, bool isBodyHTML, IEnumerable<MailAttachmentData>? attachments)
        {
            // Set up SmtpClient
            SmtpClient client = new SmtpClient();
            client.Port = port;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.EnableSsl = true;
            client.UseDefaultCredentials = false;
            client.Host = host;
            client.Credentials = new NetworkCredential(username, password);

            // Set up MailMessage
            MailMessage mail = new MailMessage();

            // Add email addresses
            if (to != null) { foreach (MailAddress email in to) { mail.To.Add(email); } }
            if (cc != null) { foreach (MailAddress email in cc) { mail.CC.Add(email); } }
            if (bcc != null) { foreach (MailAddress email in bcc) { mail.Bcc.Add(email); } }

            // Set email fields
            mail.From = from;
            mail.Subject = subject;
            mail.Body = body;
            mail.IsBodyHtml = isBodyHTML;

            if (!string.IsNullOrEmpty(messageIdHeader))
            {
                mail.Headers.Add("Message-Id", "<" + messageIdHeader + ">");
            }

            if (!string.IsNullOrEmpty(inReplyToIdHeader))
            {
                mail.Headers.Add("In-Reply-To", "<" + inReplyToIdHeader + ">");
                //mail.Headers.Add("References", "<" + inReplyToIdHeader + ">");
            }

            if (attachments != null && attachments.Any())
            {
                // Build the attachment streams and add the attachments to the email
                List<MemoryStream> streams = new List<MemoryStream>();

                foreach (MailAttachmentData attachment in attachments)
                {
                    byte[] dataBytes = Convert.FromBase64String(attachment.DataBase64 ?? "");

                    MemoryStream attachmentStream = new MemoryStream(dataBytes);

                    mail.Attachments.Add(new Attachment(attachmentStream, attachment.FileName, attachment.MimeType));

                    streams.Add(attachmentStream);
                }

                // Send the email
                client.Send(mail);

                // Dispose of the streams
                foreach (MemoryStream ms in streams)
                {
                    ms.Dispose();
                }
            }
            else
            {
                // If no attachments just send the email
                client.Send(mail);
            }
        }

        /// <summary>
        /// <para>Generates a <see cref="MailAttachmentData"/> for the specified input file, using the specified mime type.</para>
        /// <para>The result can be added to a list and then used for the attachments parameter for the several email sending functions in <see cref="EmailHelper"/>.</para>
        /// </summary>
        /// <param name="name">The name for this file. This is the filename which will appear on the attachment at the recipient's end.</param>
        /// <param name="mimeType">The mime type for the file. For example, "image/jpeg".</param>
        /// <param name="filePath">The path to the file to embed into an email.</param>
        /// <returns></returns>
        public static MailAttachmentData BuildMailAttachmentData(string name, string mimeType, string filePath)
        {
            string data = FileToBase64String(filePath);

            return new MailAttachmentData
            {
                FileName = name,
                MimeType = mimeType,
                DataBase64 = data
            };
        }

        /// <summary>
        /// Returns the file at the given <paramref name="filePath"/> as a base64 encoded string.
        /// </summary>
        /// <param name="filePath">The file to read as base64.</param>
        /// <returns></returns>
        public static string FileToBase64String(string filePath)
        {
            if (filePath is null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }
            else if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("filePath is empty string or whitespace.", nameof(filePath));
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// <para>Returns the path to the executable (without trailing \)</para>
        /// </summary>
        /// <returns></returns>
        public static string GetExecutableDirectory()
        {
            return Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory)!;
        }

        /// <summary>
        /// <para>Returns the filename (without the path) for the Entry Assembly.</para>
        /// <para>If Entry Assembly is unmanaged code, this function returns null.</para>
        /// </summary>
        /// <returns></returns>
        public static string? GetExecutableFileName()
        {
            try
            {
                return Path.GetFileName(Assembly.GetEntryAssembly()?.Location);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// <para>Returns the left <paramref name="length"/> characters of a string.</para>
        /// <para>If the string length is less than <paramref name="length"/> characters, the full string is returned.</para>
        /// </summary>
        /// <param name="input">Input string.</param>
        /// <param name="length">The length of characters to return starting from the left of <paramref name="input"/>.</param>
        /// <returns></returns>
        public static string Left(string input, int length)
        {
            input ??= string.Empty;
            return input[..Math.Min(length, input.Length)];
        }

        /// <summary>
        /// <para>Returns the right <paramref name="length"/> characters of a string.</para>
        /// <para>If the string length is less than <paramref name="length"/> characters, the full string is returned.</para>
        /// </summary>
        /// <param name="input">Input string.</param>
        /// <param name="length">The length of characters to return starting from the right of <paramref name="input"/>.</param>
        /// <returns></returns>
        public static string Right(string input, int length)
        {
            input ??= string.Empty;
            return (input.Length >= length)
                ? input.Substring(input.Length - length, length)
                : input;
        }

        /// <summary>
        /// <para>Returns the date of the first day of the current month.</para>
        /// <para>written by V4Vendetta on StackOverflow. https://stackoverflow.com/questions/5002556/set-datetime-to-start-of-month </para>
        /// </summary>
        /// <returns></returns>
        public static DateTime StartOfMonth()
        {
            DateTime today = DateTime.Today;
            return today.AddDays(1 - today.Day);
        }

        /// <summary>
        /// <para>Returns the date of the first day of the month for the given <see cref="DateTime"/>.</para>
        /// <para>Based on solution written by V4Vendetta on StackOverflow: https://stackoverflow.com/questions/5002556/set-datetime-to-start-of-month </para>
        /// </summary>
        /// <param name="dt">The input <see cref="DateTime"/> to get the start of the month for.</param>
        /// <returns></returns>
        public static DateTime StartOfMonth(DateTime dt)
        {
            return dt.Date.AddDays(1 - dt.Day);
        }

        /// <summary>
        /// <para>Compares two byte arrays for equality.</para>
        /// <para>Note: byte[] is implicitly convertible to <see cref="ReadOnlySpan&lt;byte&gt;"/>.</para>
        /// <para>https://stackoverflow.com/questions/43289/comparing-two-byte-arrays-in-net/48599119#48599119</para>
        /// </summary>
        /// <param name="a1">The first array.</param>
        /// <param name="a2">The second array.</param>
        /// <returns></returns>
        public static bool ByteArrayEqual(ReadOnlySpan<byte> a1, ReadOnlySpan<byte> a2)
        {
            // byte[] is implicitly convertible to ReadOnlySpan<byte>
            return a1.SequenceEqual(a2);
        }

        /// <summary>
        /// <para>Escapes a string so that it is safe to use in a query.</para>
        /// <para>Removes control characters except for tab, line feed and carriage return.</para>
        /// <para>Replaces single quotes with two single quotes.</para>
        /// <para>Returns escaped string.</para>
        /// </summary>
        /// <param name="str">Input string to escape.</param>
        public static string SqlGetEscapedString(string? str)
        {
            if (str == "" || str is null)
                return "";

            // Remove control characters
            // https://en.wikipedia.org/wiki/Control_character#:~:text=In%20Unicode%2C%20%22Control%2Dcharacters,in%20General%20Category%20%22Cf%22.
            str = Regex.Replace(str, @"[\u0000-\u0008\u000b\u000c\u000e-\u001F\u007F\u0080-\u009F]", string.Empty);
            return str.Replace("'", "''");
        }

        /// <summary>
        /// <para>Escapes a string so that it is safe to use in a LIKE query.</para>
        /// <para>Replaces the following characters: <paramref name="escapeCharacter"/>, %, _ and [, with <paramref name="escapeCharacter"/> then the character.</para>
        /// <para>Removes control characters except for tab, line feed and carriage return.</para>
        /// <para>Replaces single quotes with two single quotes..</para>
        /// <para>Returns escaped string.</para>
        /// </summary>
        /// <param name="str">Input string to escape.</param>
        public static string? SqlGetEscapedLikeString(string? str, char escapeCharacter = '!')
        {
            if (str == "" || str is null)
                return str;

            // https://docs.microsoft.com/en-us/sql/t-sql/language-elements/like-transact-sql?view=sql-server-ver16#pattern-matching-with-the-escape-clause
            str = str.Replace(escapeCharacter.ToString(), escapeCharacter + escapeCharacter.ToString());
            str = str.Replace("%", escapeCharacter + "%");
            str = str.Replace("_", escapeCharacter + "_");
            str = str.Replace("[", escapeCharacter + "[");

            // Remove control characters
            // https://en.wikipedia.org/wiki/Control_character#:~:text=In%20Unicode%2C%20%22Control%2Dcharacters,in%20General%20Category%20%22Cf%22.
            str = Regex.Replace(str, @"[\u0000-\u0008\u000b\u000c\u000e-\u001F\u007F\u0080-\u009F]", string.Empty);
            return str.Replace("'", "''");
        }

        /// <summary>
        /// <para>Escapes a string so that it is safe to use in a LIKE query.</para>
        /// <para>Replaces the following characters: <paramref name="escapeCharacter"/>, %, _ and [, with <paramref name="escapeCharacter"/> then the character.</para>
        /// <para>Removes control characters except for tab, line feed and carriage return.</para>
        /// <para>Returns escaped string.</para>
        /// <para>NOTE: The result of this function is intended to be used as a parameter to a parameterized query, so this function does not replace single quotes to two single quotes,
        /// which would be required for use in a raw SQL string without parameterization. For that, use <see cref="SqlGetEscapedLikeString(string, char)"/> instead.</para>
        /// </summary>
        /// <param name="str">Input string to escape.</param>
        public static string? SqlGetLikeString(string str, char escapeCharacter = '!')
        {
            if (str == "" || str is null)
                return str;

            // https://docs.microsoft.com/en-us/sql/t-sql/language-elements/like-transact-sql?view=sql-server-ver16#pattern-matching-with-the-escape-clause
            str = str.Replace(escapeCharacter.ToString(), escapeCharacter + escapeCharacter.ToString());
            str = str.Replace("%", escapeCharacter + "%");
            str = str.Replace("_", escapeCharacter + "_");
            str = str.Replace("[", escapeCharacter + "[");

            // Remove control characters
            // https://en.wikipedia.org/wiki/Control_character#:~:text=In%20Unicode%2C%20%22Control%2Dcharacters,in%20General%20Category%20%22Cf%22.
            str = Regex.Replace(str, @"[\u0000-\u0008\u000b\u000c\u000e-\u001F\u007F\u0080-\u009F]", string.Empty);
            return str;
        }

        /// <summary>
        /// <para>Escapes a string so that it is safe to use in a query.</para>
        /// <para>Replaces single quotes with two single quotes..</para>
        /// <para>Returns escaped string.</para>
        /// </summary>
        /// <param name="str">Input string to escape.</param>
        /// <param name="isDynamicSql">
        /// <para>Whether the string will be used in Dynamic SQL. If true, one single quote is replaced with four single quotes.</para>
        /// <para>Note: Dynamic SQL is a query which builds a string, and then calls either exec(@query) or exec sp_executesql @query on it, which executes the string as an SQL query, which can be vulnerable to SQL injection if special care is not taken.</para>
        /// </param>
        public static string? SqlGetEscapedString(string? str, bool isDynamicSql)
        {
            if (str == "" || str is null)
                return str;

            // Remove control characters
            // https://en.wikipedia.org/wiki/Control_character#:~:text=In%20Unicode%2C%20%22Control%2Dcharacters,in%20General%20Category%20%22Cf%22.
            str = Regex.Replace(str, @"[\u0000-\u0008\u000b\u000c\u000e-\u001F\u007F\u0080-\u009F]", string.Empty);

            if (!isDynamicSql)
                return str.Replace("'", "''");
            else
                return str.Replace("'", "''''");
        }

        /// <summary>
        /// <para>Escapes a list of strings so that they are safe to use in a query.</para>
        /// <para>Removes control characters except for tab, line feed and carriage return.</para>
        /// <para>Replaces single quotes with two single quotes.</para>
        /// <para>Returns list of escaped strings.</para>
        /// </summary>
        /// <param name="data">Input list of strings to escape.</param>
        /// <returns></returns>
        public static List<string> SqlGetEscapedList(List<string> data)
        {
            int length = data.Count;
            for (int i = 0; i < length; ++i)
            {
                if (data[i] == "" || data[i] is null)
                    continue;

                // Remove control characters
                // https://en.wikipedia.org/wiki/Control_character#:~:text=In%20Unicode%2C%20%22Control%2Dcharacters,in%20General%20Category%20%22Cf%22.
                data[i] = Regex.Replace(data[i], @"[\u0000-\u0008\u000b\u000c\u000e-\u001F\u007F\u0080-\u009F]", string.Empty);
                data[i] = data[i].Replace("'", "''");
            }
            return data;
        }

        /// <summary>
        /// <para>Escapes a list of strings so that they are safe to use in a query.</para>
        /// <para>Removes control characters except for tab, line feed and carriage return.</para>
        /// <para>Replaces single quotes with two single quotes.</para>
        /// <para>Returns list of escaped strings.</para>
        /// </summary>
        /// <param name="data">Input list of strings to escape.</param>
        /// <param name="isDynamicSql">
        /// <para>Whether the string will be used in Dynamic SQL. If true, one single quote is replaced with four single quotes.</para>
        /// <para>Note: Dynamic SQL is a query which builds a string, and then calls either exec(@query) or exec sp_executesql @query on it, which executes the string as an SQL query, which can be vulnerable to SQL injection if special care is not taken.</para>
        /// </param>
        /// <returns></returns>
        public static List<string> SqlGetEscapedList(List<string> data, bool isDynamicSql)
        {
            int length = data.Count;
            for (int i = 0; i < length; ++i)
            {
                if (data[i] == "" || data[i] is null)
                    continue;

                // Remove control characters
                // https://en.wikipedia.org/wiki/Control_character#:~:text=In%20Unicode%2C%20%22Control%2Dcharacters,in%20General%20Category%20%22Cf%22.
                data[i] = Regex.Replace(data[i], @"[\u0000-\u0008\u000b\u000c\u000e-\u001F\u007F\u0080-\u009F]", string.Empty);

                if (!isDynamicSql)
                    data[i] = data[i].Replace("'", "''");
                else
                    data[i] = data[i].Replace("'", "''''");
            }
            return data;
        }

        /// <summary>
        /// <para>Takes an IEnumerable&lt;string&gt;, calls GetEscapedString() on each, and returns a string containing the list with each item in quotes, each separated by a comma and newline.</para>
        /// <para>Intended to be used when building the "where x in ( y, z )" part of an SQL query.</para>
        /// <para>e.g. string[] { "One", "Twos", "Three's", "Four" }</para>
        /// <para>would return 'One',\n'Twos',\n'Three''s'\n,'Four'</para>
        /// </summary>
        /// <param name="items">List of strings to use to create the output.</param>
        /// <returns></returns>
        public static string SqlBuildEscapedInString(IEnumerable<string> items)
        {
            StringBuilder sb = new StringBuilder();

            using (IEnumerator<string> i = items.GetEnumerator())
            {
                i.MoveNext();

                while (true)
                {
                    sb.Append('\'');
                    sb.Append(SqlGetEscapedString(i.Current));
                    sb.Append('\'');

                    if (!i.MoveNext())
                        break;

                    sb.AppendLine(",");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// <para>Takes an IEnumerable&lt;string&gt;, calls GetEscapedString() on each, and returns a string containing the list with each item in quotes, each separated by a comma and newline.</para>
        /// <para>Intended to be used when building the "where x in ( y, z )" part of an SQL query.</para>
        /// <para>e.g. string[] { "One", "Twos", "Three's", "Four" }</para>
        /// <para>would return 'One',\n'Twos',\n'Three''s'\n,'Four'</para>
        /// </summary>
        /// <param name="items">List of strings to use to create the output.</param>
        /// <param name="isDynamicSql">
        /// <para>Whether the string will be used in Dynamic SQL. If true, one single quote is replaced with four single quotes.</para>
        /// <para>Note: Dynamic SQL is a query which builds a string, and then calls either exec(@query) or exec sp_executesql @query on it, which executes the string as an SQL query, which can be vulnerable to SQL injection if special care is not taken.</para>
        /// </param>
        /// <returns></returns>
        public static string SqlBuildEscapedInString(IEnumerable<string> items, bool isDynamicSql)
        {
            StringBuilder sb = new StringBuilder();

            using (IEnumerator<string> i = items.GetEnumerator())
            {
                i.MoveNext();

                while (true)
                {
                    if (isDynamicSql)
                    {
                        sb.Append("''");
                        sb.Append(SqlGetEscapedString(i.Current, isDynamicSql));
                        sb.Append("''");
                    }
                    else
                    {
                        sb.Append('\'');
                        sb.Append(SqlGetEscapedString(i.Current));
                        sb.Append('\'');
                    }

                    if (!i.MoveNext())
                        break;

                    sb.AppendLine(",");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// <para>Takes an <see cref="IEnumerable{string}"/>, calls <see cref="SqlGetEscapedString(string)"/> on each, and returns a string containing the list with each item in quotes, each separated by a comma and newline.</para>
        /// <para>Intended to be used when building the "where x in ( y, z )" part of an SQL query.</para>
        /// <para>e.g. string[] { "One", "Twos", "Three's", "Four" }</para>
        /// <para>would return 'One',\n'Twos',\n'Three''s'\n,'Four'</para>
        /// </summary>
        /// <param name="items">List of strings to use to create the output.</param>
        /// <returns></returns>
        public static string SqlBuildEscapedInString<T>(IEnumerable<T> items)
        {
            StringBuilder sb = new StringBuilder();

            using (IEnumerator<T> i = items.GetEnumerator())
            {
                i.MoveNext();

                while (true)
                {
                    if (i.Current is null)
                    {
                        sb.Append("null");
                    }
                    else
                    {
                        sb.Append('\'');
                        sb.Append(SqlGetEscapedString(i.Current.ToString()));
                        sb.Append('\'');
                    }

                    if (!i.MoveNext())
                        break;

                    sb.AppendLine(",");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// <para>Takes an <see cref="IEnumerable{T}"/>, calls ToString() then <see cref="SqlGetEscapedString(string)"/> on each, and returns a string containing the list with each item in quotes, each separated by a comma and newline.</para>
        /// <para>Intended to be used when building the "where x in ( y, z )" part of an SQL query.</para>
        /// <para>e.g. string[] { "One", "Twos", "Three's", "Four" }</para>
        /// <para>would return 'One',\n'Twos',\n'Three''s'\n,'Four'</para>
        /// </summary>
        /// <param name="items">List of strings to use to create the output.</param>
        /// <param name="isDynamicSql">
        /// <para>Whether the string will be used in Dynamic SQL. If true, one single quote is replaced with four single quotes.</para>
        /// <para>Note: Dynamic SQL is a query which builds a string, and then calls either exec(@query) or exec sp_executesql @query on it, which executes the string as an SQL query, which can be vulnerable to SQL injection if special care is not taken.</para>
        /// </param>
        /// <returns></returns>
        public static string SqlBuildEscapedInString<T>(IEnumerable<T> items, bool isDynamicSql)
        {
            StringBuilder sb = new StringBuilder();

            using (IEnumerator<T> i = items.GetEnumerator())
            {
                i.MoveNext();

                while (true)
                {
                    if (i.Current is null)
                    {
                        sb.Append("null");
                    }
                    else if (isDynamicSql)
                    {
                        sb.Append("''");
                        sb.Append(SqlGetEscapedString(i.Current.ToString(), isDynamicSql));
                        sb.Append("''");
                    }
                    else
                    {
                        sb.Append('\'');
                        sb.Append(SqlGetEscapedString(i.Current.ToString()));
                        sb.Append('\'');
                    }

                    if (!i.MoveNext())
                        break;

                    sb.AppendLine(",");
                }
            }

            return sb.ToString();
        }

        public static long? ParseNullableInt64(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return null;
            }

            return long.TryParse(input, out long outValue) ? outValue : null;
        }

        public static Guid? ParseNullableGuid(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return null;
            }

            return Guid.TryParse(input, out Guid outValue) ? outValue : null;
        }

        public static string GuidToBase64String(Guid id)
        {
            Span<byte> idBytes = stackalloc byte[16];
            Span<byte> base64Bytes = stackalloc byte[24];

            MemoryMarshal.TryWrite(idBytes, in id);

            Base64.EncodeToUtf8(idBytes, base64Bytes, out _, out _);

            Span<char> finalChars = stackalloc char[22];

            for (int i = 0; i < 22; ++i)
            {
                finalChars[i] = base64Bytes[i] switch
                {
                    (byte)'/' => '-',
                    (byte)'+' => '_',
                    _ => (char)base64Bytes[i]
                };
            }

            return new string(finalChars);
        }

        /// <summary>
        /// <para>Input string should be 22 character base64 string generated using <see cref="GuidToBase64String(Guid)"/>.</para>
        /// <para>Returns <see cref="Guid.Empty"/> if input string is not 22 characters long.</para>
        /// </summary>
        /// <param name="id">22 character base64 string generated using <see cref="GuidToBase64String(Guid)"/>.</param>
        /// <returns></returns>
        public static Guid Base64StringToGuid(ReadOnlySpan<char> id)
        {
            if (id.Length != 22)
            {
                return Guid.Empty;
            }

            Span<char> base64Chars = stackalloc char[24];

            for (int i = 0; i < 22; ++i)
            {
                base64Chars[i] = id[i] switch
                {
                    '-' => '/', // hyphen becomes slash
                    '_' => '+', // underscore becomes plus
                    _ => id[i]
                };
            }

            base64Chars[22] = '=';
            base64Chars[23] = '=';

            Span<byte> idBytes = stackalloc byte[16];

            Convert.TryFromBase64Chars(base64Chars, idBytes, out _);

            return new Guid(idBytes);
        }

        /// <summary>
        /// <para>Input string should be 22 character base64 string generated using <see cref="GuidToBase64String(Guid)"/>.</para>
        /// </summary>
        /// <param name="id">22 character base64 string generated using <see cref="GuidToBase64String(Guid)"/>.</param>
        /// <param name="result"></param>
        /// <returns></returns>
        public static Guid? ParseBase64StringToNullableGuid(ReadOnlySpan<char> id)
        {
            if (id.Length != 22)
            {
                return null;
            }

            Span<char> base64Chars = stackalloc char[24];

            for (int i = 0; i < 22; ++i)
            {
                base64Chars[i] = id[i] switch
                {
                    '-' => '/', // hyphen becomes slash
                    '_' => '+', // underscore becomes plus
                    _ => id[i]
                };
            }

            base64Chars[22] = '=';
            base64Chars[23] = '=';

            Span<byte> idBytes = stackalloc byte[16];

            Convert.TryFromBase64Chars(base64Chars, idBytes, out _);

            return new Guid(idBytes);
        }

        /// <summary>
        /// <para>Input string should be 22 character base64 string generated using <see cref="GuidToBase64String(Guid)"/>.</para>
        /// </summary>
        /// <param name="id">22 character base64 string generated using <see cref="GuidToBase64String(Guid)"/>.</param>
        /// <param name="result"></param>
        /// <returns></returns>
        public static bool TryParseBase64StringToGuid(ReadOnlySpan<char> id, out Guid? result)
        {
            if (id.Length != 22)
            {
                result = null;
                return false;
            }

            Span<char> base64Chars = stackalloc char[24];

            for (int i = 0; i < 22; ++i)
            {
                base64Chars[i] = id[i] switch
                {
                    '-' => '/', // hyphen becomes slash
                    '_' => '+', // underscore becomes plus
                    _ => id[i]
                };
            }

            base64Chars[22] = '=';
            base64Chars[23] = '=';

            Span<byte> idBytes = stackalloc byte[16];

            Convert.TryFromBase64Chars(base64Chars, idBytes, out _);

            result = new Guid(idBytes);

            return true;
        }

        /// <summary>
        /// Executes a query using Dapper and returns the result as a <see cref="DataTable"/>.
        /// </summary>
        /// <param name="sql">The query to run on the database.</param>
        /// <param name="sqlConnection">The connection to the database to be used to run the query. The connection will be opened if it is not already.</param>
        /// <returns></returns>
        public static DataTable GetDataTableFromSql(string sql, SqlConnection sqlConnection)
        {
            using (IDataReader dataReader = sqlConnection.ExecuteReader(sql))
            {
                DataTable dt = new DataTable();
                dt.Load(dataReader);

                foreach (DataColumn col in dt.Columns)
                {
                    col.ReadOnly = false;
                    col.MaxLength = -1;
                }

                return dt;
            }
        }

        /// <summary>
        /// Executes a query using Dapper and returns the result as a <see cref="DataTable"/>.
        /// </summary>
        /// <param name="sql">The query to run on the database.</param>
        /// <param name="sqlConnection">The connection to the database to be used to run the query. The connection will be opened if it is not already.</param>
        /// <returns></returns>
        public static async Task<DataTable> GetDataTableFromSqlAsync(string sql, SqlConnection sqlConnection)
        {
            using (IDataReader dataReader = await sqlConnection.ExecuteReaderAsync(sql))
            {
                DataTable dt = new DataTable();
                dt.Load(dataReader);

                foreach (DataColumn col in dt.Columns)
                {
                    col.ReadOnly = false;
                    col.MaxLength = -1;
                }

                return dt;
            }
        }

        /// <summary>
        /// Executes a query using Dapper and returns the result as a <see cref="DataTable"/>.
        /// </summary>
        /// <param name="sql">The query to run on the database.</param>
        /// <param name="dynamicParameters">The <see cref="DynamicParameters"/> to use with the query.</param>
        /// <param name="sqlConnection">The connection to the database to be used to run the query. The connection will be opened if it is not already.</param>
        /// <returns></returns>
        public static DataTable GetDataTableFromSql(string sql, DynamicParameters dynamicParameters, SqlConnection sqlConnection)
        {
            using (IDataReader dataReader = sqlConnection.ExecuteReader(sql, dynamicParameters))
            {
                DataTable dt = new DataTable();
                dt.Load(dataReader);

                foreach (DataColumn col in dt.Columns)
                {
                    col.ReadOnly = false;
                    col.MaxLength = -1;
                }

                return dt;
            }
        }

        /// <summary>
        /// Executes a query using Dapper and returns the result as a <see cref="DataTable"/>.
        /// </summary>
        /// <param name="sql">The query to run on the database.</param>
        /// <param name="dynamicParameters">The <see cref="DynamicParameters"/> to use with the query.</param>
        /// <param name="sqlConnection">The connection to the database to be used to run the query. The connection will be opened if it is not already.</param>
        /// <returns></returns>
        public static async Task<DataTable> GetDataTableFromSqlAsync(string sql, DynamicParameters dynamicParameters, SqlConnection sqlConnection)
        {
            using (IDataReader dataReader = await sqlConnection.ExecuteReaderAsync(sql, dynamicParameters))
            {
                DataTable dt = new DataTable();
                dt.Load(dataReader);

                foreach (DataColumn col in dt.Columns)
                {
                    col.ReadOnly = false;
                    col.MaxLength = -1;
                }

                return dt;
            }
        }

        /// <summary>
        /// Executes a query using Dapper and returns the result as a <see cref="DataTable"/>.
        /// </summary>
        /// <param name="commandDefinition">The <see cref="CommandDefinition"/> containing the query, parameters, as well as anything else such as the <see cref="CancellationToken"/>.</param>
        /// <param name="sqlConnection">The connection to the database to be used to run the query. The connection will be opened if it is not already.</param>
        /// <returns></returns>
        public static DataTable GetDataTableFromSql(CommandDefinition commandDefinition, SqlConnection sqlConnection)
        {
            using (IDataReader dataReader = sqlConnection.ExecuteReader(commandDefinition))
            {
                DataTable dt = new DataTable();
                dt.Load(dataReader);

                foreach (DataColumn col in dt.Columns)
                {
                    col.ReadOnly = false;
                    col.MaxLength = -1;
                }

                return dt;
            }
        }

        /// <summary>
        /// Executes a query using Dapper and returns the result as a <see cref="DataTable"/>.
        /// </summary>
        /// <param name="commandDefinition">The <see cref="CommandDefinition"/> containing the query, parameters, as well as anything else such as the <see cref="CancellationToken"/>.</param>
        /// <param name="sqlConnection">The connection to the database to be used to run the query. The connection will be opened if it is not already.</param>
        /// <returns></returns>
        public static async Task<DataTable> GetDataTableFromSqlAsync(CommandDefinition commandDefinition, SqlConnection sqlConnection)
        {
            using (IDataReader dataReader = await sqlConnection.ExecuteReaderAsync(commandDefinition))
            {
                DataTable dt = new DataTable();
                dt.Load(dataReader);

                foreach (DataColumn col in dt.Columns)
                {
                    col.ReadOnly = false;
                    col.MaxLength = -1;
                }

                return dt;
            }
        }

        /// <summary>
        /// <para>Loads a result from an <see cref="IDataReader"/> into a <see cref="DataTable"/> and returns it.</para>
        /// <para>The IDataReader will be advanced to the next result after reading.</para>
        /// </summary>
        /// <param name="dataReader"><see cref="IDataReader"/> to load the current result into a <see cref="DataTable"/>.</param>
        /// <returns></returns>
        public static DataTable GetDataTableFromDataReader(IDataReader dataReader)
        {
            DataTable dt = new DataTable();
            dt.Load(dataReader);

            foreach (DataColumn col in dt.Columns)
            {
                col.ReadOnly = false;
                col.MaxLength = -1;
            }

            return dt;
        }

        /// <summary>
        /// <para>Converts a <see cref="DataTable"/> to a JSON string.</para>
        /// </summary>
        /// <param name="dataTable"><see cref="DataTable"/> to convert to a JSON string.</param>
        /// <returns></returns>
        public static string DataTableToJson(DataTable dataTable, JsonSerializerOptions? options = null)
        {
            IEnumerable<Dictionary<string, object>> data = dataTable.Rows.OfType<DataRow>()
                .Select(row => dataTable.Columns.OfType<DataColumn>()
                    .ToDictionary(col => col.ColumnName, c => row[c]));

            return JsonSerializer.Serialize(data, options);
        }

        /// <summary>
        /// <para>Escapes a column/table name so that it is safe to use.</para>
        /// <para>Adds [ and ] to the start and end and replaces ] with ]].</para>
        /// <para>Keeps only characters between chr(32) to chr(127).</para>
        /// <para>Returns escaped string.</para>
        /// <para>Note: Input string length should not be greater than 128 characters due to MSSQL naming limitations.</para>
        /// </summary>
        /// <param name="str">Input string to escape.</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static string SqlGetQuoteName(string str)
        {
            if (str == "" || str is null)
                return "";

            if (str.Length > 128)
            {
                throw new ArgumentException("Input string length should not be greater than 128 characters.", nameof(str));
            }

            str = GeneratedRegexes.NonPrintableAscii().Replace(str, string.Empty);
            str = "[" + str.Replace("]", "]]") + "]";

            return str;
        }

        /// <summary>
        /// <para>Updates the <paramref name="columnName"/> column in the <paramref name="tableName"/> table, to change the given guids to new ones using <see cref="RT.Comb.EnsureOrderedProvider.Sql.Create"/>.</para>
        /// <para>Returns a list of the old and new guids.</para>
        /// </summary>
        /// <param name="sqlConnection"></param>
        /// <param name="guids"></param>
        /// <param name="tableName"></param>
        /// <param name="columnName"></param>
        /// <returns></returns>
        public static async Task<List<GuidPair>> UpdateLogGuidsWithRTCombAsync(SqlConnection sqlConnection, List<Guid> guids, string tableName, string columnName = "id")
        {
            if (guids is null || guids.Count == 0)
            {
                return new List<GuidPair>();
            }

            int guidsCount = guids.Count;

            List<GuidPair> guidMap = new List<GuidPair>();

            foreach (Guid guid in guids)
            {
                guidMap.Add(new GuidPair(guid, RT.Comb.EnsureOrderedProvider.Sql.Create()));
            }

            string quotedTableName = SqlGetQuoteName(tableName);
            string quotedColumnName = SqlGetQuoteName(columnName);

            int processedTotal = 0;

            // Process in batches of 1000 items because MSSQL parameter limit is 2100 (1000 rows * 2 guids = 2000)
            int itemsPerBatch = 1000;

            while (processedTotal < guidsCount)
            {
                DynamicParameters parameters = new DynamicParameters();
                int toProcessCurrent = Math.Min(itemsPerBatch, guids.Count - processedTotal);

                // Estimate capacity to store the full query. ~365 chars = fixed part of query, 24 chars = variable part
                int estimatedCapacity = 365 + (toProcessCurrent * 24);

                StringBuilder sb = new StringBuilder($@"
declare @_data table
(
    OldId uniqueidentifier NOT NULL
   ,NewId uniqueidentifier NOT NULL
)
", estimatedCapacity);

                if (toProcessCurrent > 0)
                {
                    sb.AppendLine($"insert into @_data (OldId, NewId) values");

                    for (int i = 0; i < toProcessCurrent; ++i)
                    {
                        if (i > 0)
                        {
                            sb.Append(',');
                        }

                        sb.AppendLine($"(@oldId{i}, @newId{i})");
                        parameters.Add($"oldId{i}", guidMap[processedTotal].OldId, DbType.Guid, ParameterDirection.Input);
                        parameters.Add($"newId{i}", guidMap[processedTotal].NewId, DbType.Guid, ParameterDirection.Input);
                        ++processedTotal;
                    }
                }

                sb.AppendLine($@"
update LiveTable
set LiveTable.{quotedColumnName} = TempTable.NewId
from {quotedTableName} as LiveTable
inner join @_data as TempTable
on LiveTable.{quotedColumnName} = TempTable.OldId
");

                await sqlConnection.ExecuteAsync(sb.ToString(), parameters);
            }

            return guidMap;
        }

        /// <summary>
        /// <para>Updates the <paramref name="columnName"/> column in the <paramref name="tableName"/> table, to change the given guids to new ones using <see cref="RT.Comb.EnsureOrderedProvider.Sql.Create"/>.</para>
        /// <para>Uses <see cref="BulkUploadToSql{T}"/> for uploading data.</para>
        /// <para>Returns a list of the old and new guids.</para>
        /// </summary>
        /// <param name="sqlConnection"></param>
        /// <param name="guids"></param>
        /// <param name="tableName"></param>
        /// <param name="columnName"></param>
        /// <returns></returns>
        public static async Task<List<GuidPair>> UpdateLogGuidsWithRTCombBulkUploadAsync(SqlConnection sqlConnection, List<Guid> guids, string tableName, string columnName = "id")
        {
            if (guids is null || guids.Count == 0)
            {
                return new List<GuidPair>();
            }

            List<GuidPair> guidMap = new List<GuidPair>();

            foreach (Guid guid in guids)
            {
                guidMap.Add(new GuidPair(guid, RT.Comb.EnsureOrderedProvider.Sql.Create()));
            }

            string tempTableNameUnquoted = $"{tableName}_temp_{DateTime.Now.Ticks}_{Random.Shared.Next()}";
            string tempTableName = SqlGetQuoteName(tempTableNameUnquoted);
            string tempTablePK = SqlGetQuoteName("PK_" + tempTableNameUnquoted);
            string liveTableName = SqlGetQuoteName(tableName);
            string quotedColumnName = SqlGetQuoteName(columnName);

            string sql = $@"
CREATE TABLE [dbo].{tempTableName}(
	[OldId] [uniqueidentifier] NOT NULL,
	[NewId] [uniqueidentifier] NOT NULL,
 CONSTRAINT {tempTablePK} PRIMARY KEY CLUSTERED 
(
	[OldId] ASC,
	[NewId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
";
            await sqlConnection.ExecuteAsync(sql);

            // Insert into temporary table
            BulkUploadToSql<GuidPair> bulkUploadToSql = new BulkUploadToSql<GuidPair>
            {
                InternalStore = guidMap,
                TableName = tempTableName
            };

            await bulkUploadToSql.CommitAsync(sqlConnection);

            // Update live table using data from temporary table, then drop temporary table
            sql = $@"
UPDATE
    LiveTable
SET
    LiveTable.{quotedColumnName} = TempTable.NewId
FROM
    {liveTableName} AS LiveTable
    INNER JOIN {tempTableName} AS TempTable
        ON LiveTable.{quotedColumnName} = TempTable.OldId

drop table {tempTableName}
";
            await sqlConnection.ExecuteAsync(sql);

            return guidMap;
        }

        /// <summary>
        /// <para>Updates the "id" column in each of the given tables, to change the given guids to new ones using <see cref="RT.Comb.EnsureOrderedProvider.Sql.Create"/>.</para>
        /// <para>Returns a list of the old and new guids for each table.</para>
        /// </summary>
        /// <param name="sqlConnection"></param>
        /// <param name="guids"></param>
        /// <param name="tableNames"></param>
        /// <param name="tempTablePrefix"></param>
        /// <returns></returns>
        public static async Task<List<TableGuidPair>> UpdateLogGuidsWithRTCombAsync(SqlConnection sqlConnection, List<(string tableName, Guid)> guids, List<string> tableNames, string tempTablePrefix)
        {
            if (guids is null || guids.Count == 0)
            {
                return new List<TableGuidPair>();
            }

            string tempTableNameUnquoted = $"{tempTablePrefix}_temp_{DateTime.Now.Ticks}_{Random.Shared.Next()}";
            string tempTableName = SqlGetQuoteName(tempTableNameUnquoted);
            string tempTablePK = SqlGetQuoteName("PK_" + tempTableNameUnquoted);

            List<TableGuidPair> guidMap = new List<TableGuidPair>();

            foreach ((string tableName, Guid guid) in guids)
            {
                guidMap.Add(new TableGuidPair
                {
                    TableName = tableName,
                    OldId = guid,
                    NewId = RT.Comb.EnsureOrderedProvider.Sql.Create()
                });
            }

            // Create temporary table to store the data
            string sql = $@"
CREATE TABLE [dbo].{tempTableName}(
	[TableName] [nvarchar](128) NOT NULL,
	[OldId] [uniqueidentifier] NOT NULL,
	[NewId] [uniqueidentifier] NOT NULL,
 CONSTRAINT {tempTablePK} PRIMARY KEY CLUSTERED 
(
	[TableName] ASC,
	[OldId] ASC,
	[NewId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
";
            await sqlConnection.ExecuteAsync(sql);

            // Insert into temporary table
            BulkUploadToSql<TableGuidPair> bulkUploadToSql = new BulkUploadToSql<TableGuidPair>
            {
                InternalStore = guidMap,
                TableName = tempTableName
            };

            await bulkUploadToSql.CommitAsync(sqlConnection);

            // Update live table using data from temporary table, then drop temporary table
            StringBuilder sb = new StringBuilder();

            foreach (string tableName in tableNames)
            {
                string quotedLiveTableName = SqlGetQuoteName(tableName);
                string escapedLiveTableName = SqlGetEscapedString(tableName);

                sb.AppendLine($@"
UPDATE
    LiveTable
SET
    LiveTable.id = TempTable.NewId
FROM
    {quotedLiveTableName} AS LiveTable
    INNER JOIN {tempTableName} AS TempTable
        ON LiveTable.id = TempTable.OldId
        AND TempTable.TableName = '{escapedLiveTableName}'
");
            }

            sb.AppendLine($"drop table {tempTableName}");

            await sqlConnection.ExecuteAsync(sb.ToString());

            return guidMap;
        }

        /// <summary>
        /// <para>Updates the given column in each of the given tables, to change the given guids to new ones using <see cref="RT.Comb.EnsureOrderedProvider.Sql.Create"/>.</para>
        /// <para>Returns a list of the old and new guids for each table.</para>
        /// </summary>
        /// <param name="sqlConnection"></param>
        /// <param name="guids"></param>
        /// <param name="tempTablePrefix"></param>
        /// <returns></returns>
        public static async Task<List<TableGuidPair>> UpdateLogGuidsWithRTCombAsync(SqlConnection sqlConnection, List<(string tableName, string columnName, Guid)> guids, List<string> tableNames, string tempTablePrefix)
        {
            if (guids is null || guids.Count == 0)
            {
                return new List<TableGuidPair>();
            }

            string tempTableNameUnquoted = $"{tempTablePrefix}_temp_{DateTime.Now.Ticks}_{Random.Shared.Next()}";
            string tempTableName = SqlGetQuoteName(tempTableNameUnquoted);
            string tempTablePK = SqlGetQuoteName("PK_" + tempTableNameUnquoted);

            List<TableGuidPair> guidMap = new List<TableGuidPair>();

            foreach ((string tableName, string columnName, Guid guid) in guids)
            {
                guidMap.Add(new TableGuidPair
                {
                    TableName = tableName,
                    OldId = guid,
                    NewId = RT.Comb.EnsureOrderedProvider.Sql.Create()
                });
            }

            // Create temporary table to store the data
            string sql = $@"
CREATE TABLE [dbo].{tempTableName}(
	[TableName] [nvarchar](128) NOT NULL,
	[OldId] [uniqueidentifier] NOT NULL,
	[NewId] [uniqueidentifier] NOT NULL,
 CONSTRAINT {tempTablePK} PRIMARY KEY CLUSTERED 
(
	[TableName] ASC,
	[OldId] ASC,
	[NewId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
";
            await sqlConnection.ExecuteAsync(sql);

            // Insert into temporary table
            BulkUploadToSql<TableGuidPair> bulkUploadToSql = new BulkUploadToSql<TableGuidPair>
            {
                InternalStore = guidMap,
                TableName = tempTableName
            };

            await bulkUploadToSql.CommitAsync(sqlConnection);

            // Update live table using data from temporary table, then drop temporary table
            StringBuilder sb = new StringBuilder();

            foreach (string tableName in tableNames)
            {
                string quotedLiveTableName = SqlGetQuoteName(tableName);
                string escapedLiveTableName = SqlGetEscapedString(tableName);

                sb.AppendLine($@"
UPDATE
    LiveTable
SET
    LiveTable.id = TempTable.NewId
FROM
    {quotedLiveTableName} AS LiveTable
    INNER JOIN {tempTableName} AS TempTable
        ON LiveTable.id = TempTable.OldId
        AND TempTable.TableName = '{escapedLiveTableName}'
");
            }

            sb.AppendLine($"drop table {tempTableName}");

            await sqlConnection.ExecuteAsync(sb.ToString());

            return guidMap;
        }

        public static async Task UpdateGuidsUsingGuidMapAsync(SqlConnection sqlConnection, List<GuidPair> guidMap,
            UpdateGuidsUsingGuidMapDestinationColumnOptions destinationColumnOptions)
        {
            if (guidMap is null || guidMap.Count == 0)
            {
                return;
            }

            string tempTableNameUnquoted = $"{destinationColumnOptions.TableName}_temp_{DateTime.Now.Ticks}_{Random.Shared.Next()}";
            string tempTableName = SqlGetQuoteName(tempTableNameUnquoted);
            string tempTablePK = SqlGetQuoteName("PK_" + tempTableNameUnquoted);
            string liveTableName = SqlGetQuoteName(destinationColumnOptions.TableName);
            string quotedColumnName = SqlGetQuoteName(destinationColumnOptions.ColumnName);

            string sql = $@"
CREATE TABLE [dbo].{tempTableName}(
	[OldId] [uniqueidentifier] NOT NULL,
	[NewId] [uniqueidentifier] NOT NULL,
 CONSTRAINT {tempTablePK} PRIMARY KEY CLUSTERED 
(
	[OldId] ASC,
	[NewId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
";
            await sqlConnection.ExecuteAsync(sql);

            // Insert into temporary table
            BulkUploadToSql<GuidPair> bulkUploadToSql = new BulkUploadToSql<GuidPair>
            {
                InternalStore = guidMap,
                TableName = tempTableName
            };

            await bulkUploadToSql.CommitAsync(sqlConnection);

            string tempTableNewIdString = "TempTable.NewId";
            string tempTableOldIdString = "TempTable.OldId";

            switch (destinationColumnOptions.DbType)
            {
                case DbType.Guid:
                    // Do nothing
                    break;
                case DbType.String:
                    if (destinationColumnOptions.Size is null || destinationColumnOptions.Size <= 0)
                    {
                        throw new ArgumentException($"When DestinationColumnOptions.DbType is String or AnsiString, DestinationColumnOptions.Size must be an integer greater than zero.", nameof(destinationColumnOptions));
                    }
                    tempTableNewIdString = $"lower(convert(nvarchar({destinationColumnOptions.Size}), TempTable.NewId))";
                    tempTableOldIdString = $"lower(convert(nvarchar({destinationColumnOptions.Size}), TempTable.OldId))";
                    break;
                case DbType.AnsiString:
                    if (destinationColumnOptions.Size is null || destinationColumnOptions.Size <= 0)
                    {
                        throw new ArgumentException($"When DestinationColumnOptions.DbType is String or AnsiString, DestinationColumnOptions.Size must be an integer greater than zero.", nameof(destinationColumnOptions));
                    }
                    tempTableNewIdString = $"lower(convert(varchar({destinationColumnOptions.Size}), TempTable.NewId))";
                    tempTableOldIdString = $"lower(convert(varchar({destinationColumnOptions.Size}), TempTable.OldId))";
                    break;
                default:
                    throw new ArgumentException($"Unsupported DbType: {destinationColumnOptions.DbType}", nameof(destinationColumnOptions));
            }

            // Update live table using data from temporary table, then drop temporary table
            sql = $@"
UPDATE
    LiveTable
SET
    LiveTable.{quotedColumnName} = {tempTableNewIdString}
FROM
    {liveTableName} AS LiveTable
    INNER JOIN {tempTableName} AS TempTable
        ON LiveTable.{quotedColumnName} = {tempTableOldIdString}

drop table {tempTableName}
";
            await sqlConnection.ExecuteAsync(sql);
        }

        public static async Task UpdateGuidsUsingGuidMapAsync(SqlConnection sqlConnection,
            List<TableColumnGuidPair> guidMap,
            List<UpdateGuidsUsingGuidMapDestinationColumnOptions> destinationColumnOptions,
            string tempTablePrefix)
        {
            if (guidMap is null || guidMap.Count == 0)
            {
                return;
            }

            string tempTableNameUnquoted = $"{tempTablePrefix}_temp_{DateTime.Now.Ticks}_{Random.Shared.Next()}";
            string tempTableName = SqlGetQuoteName(tempTableNameUnquoted);
            string tempTablePK = SqlGetQuoteName("PK_" + tempTableNameUnquoted);

            // Create temporary table to store the data
            string sql = $@"
CREATE TABLE [dbo].{tempTableName}(
	[TableName] [nvarchar](128) NOT NULL,
	[ColumnName] [nvarchar](128) NOT NULL,
	[OldId] [uniqueidentifier] NOT NULL,
	[NewId] [uniqueidentifier] NOT NULL,
 CONSTRAINT {tempTablePK} PRIMARY KEY CLUSTERED 
(
	[TableName] ASC,
	[ColumnName] ASC,
	[OldId] ASC,
	[NewId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
) ON [PRIMARY]
";
            await sqlConnection.ExecuteAsync(sql);

            // Insert into temporary table
            BulkUploadToSql<TableColumnGuidPair> bulkUploadToSql = new BulkUploadToSql<TableColumnGuidPair>
            {
                InternalStore = guidMap,
                TableName = tempTableName
            };

            await bulkUploadToSql.CommitAsync(sqlConnection);

            // Update live table using data from temporary table, then drop temporary table
            StringBuilder sb = new StringBuilder();

            foreach (UpdateGuidsUsingGuidMapDestinationColumnOptions destinationColumnOpts in destinationColumnOptions)
            {
                string quotedLiveTableName = SqlGetQuoteName(destinationColumnOpts.TableName);
                string escapedLiveTableName = SqlGetEscapedString(destinationColumnOpts.TableName);
                string quotedColumnName = SqlGetQuoteName(destinationColumnOpts.ColumnName);
                string escapedColumnName = SqlGetEscapedString(destinationColumnOpts.ColumnName);

                string tempTableNewIdString = "TempTable.NewId";
                string tempTableOldIdString = "TempTable.OldId";

                switch (destinationColumnOpts.DbType)
                {
                    case DbType.Guid:
                        // Do nothing
                        break;
                    case DbType.String:
                        if (destinationColumnOpts.Size is null || destinationColumnOpts.Size <= 0)
                        {
                            throw new ArgumentException($"When DestinationColumnOptions.DbType is String or AnsiString, DestinationColumnOptions.Size must be an integer greater than zero.", nameof(destinationColumnOpts));
                        }
                        tempTableNewIdString = $"lower(convert(nvarchar({destinationColumnOpts.Size}), TempTable.NewId))";
                        tempTableOldIdString = $"lower(convert(nvarchar({destinationColumnOpts.Size}), TempTable.OldId))";
                        break;
                    case DbType.AnsiString:
                        if (destinationColumnOpts.Size is null || destinationColumnOpts.Size <= 0)
                        {
                            throw new ArgumentException($"When DestinationColumnOptions.DbType is String or AnsiString, DestinationColumnOptions.Size must be an integer greater than zero.", nameof(destinationColumnOpts));
                        }
                        tempTableNewIdString = $"lower(convert(varchar({destinationColumnOpts.Size}), TempTable.NewId))";
                        tempTableOldIdString = $"lower(convert(varchar({destinationColumnOpts.Size}), TempTable.OldId))";
                        break;
                    default:
                        throw new ArgumentException($"Unsupported DbType: {destinationColumnOpts.DbType}", nameof(destinationColumnOpts));
                }

                sb.AppendLine($@"
UPDATE
    LiveTable
SET
    LiveTable.{quotedColumnName} = {tempTableNewIdString}
FROM
    {quotedLiveTableName} AS LiveTable
    INNER JOIN {tempTableName} AS TempTable
        ON LiveTable.{quotedColumnName} = {tempTableOldIdString}
        AND TempTable.TableName = '{escapedLiveTableName}'
        AND TempTable.ColumnName = '{escapedColumnName}'
");
            }

            sb.AppendLine($"drop table {tempTableName}");

            await sqlConnection.ExecuteAsync(sb.ToString());
        }

        /// <summary>
        /// <para>Takes a <see cref="List{T}"/> and returns a new <see cref="List{T}"/> with duplicates and nulls removed.</para>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="items"></param>
        /// <returns></returns>
        public static List<T> DedupeList<T>(List<T> items)
        {
            List<T> results = new List<T>();

            if (items.Count == 0)
                return results;

            foreach (T item in items)
            {
                if (item is null)
                {
                    continue;
                }

                if (!results.Contains(item))
                {
                    results.Add(item);
                }
            }

            return results;
        }

        /// <summary>
        /// <para>Takes a <see cref="List{Guid}"/> and returns a new <see cref="List{Guid}"/> with duplicates and <see cref="Guid.Empty"/> removed.</para>
        /// </summary>
        /// <param name="items"></param>
        /// <returns></returns>
        public static List<Guid> DedupeGuidList(List<Guid> items)
        {
            List<Guid> results = new List<Guid>();

            if (items.Count == 0)
                return results;

            foreach (Guid item in items)
            {
                if (item == Guid.Empty)
                {
                    continue;
                }

                if (!results.Contains(item))
                {
                    results.Add(item);
                }
            }

            return results;
        }

        /// <summary>
        /// <para>Takes a <see cref="List{Guid?}"/> and returns a new <see cref="List{Guid}"/> with duplicates, <see cref="Guid.Empty"/> and nulls removed.</para>
        /// </summary>
        /// <param name="items"></param>
        /// <returns></returns>
        public static List<Guid> DedupeGuidList(List<Guid?> items)
        {
            List<Guid> results = new List<Guid>();

            if (items.Count == 0)
                return results;

            foreach (Guid? item in items)
            {
                if (item is null || item == Guid.Empty)
                {
                    continue;
                }

                if (!results.Contains(item.Value))
                {
                    results.Add(item.Value);
                }
            }

            return results;
        }

        /// <summary>
        /// <para>Takes a <see cref="List{string}"/> and returns a new <see cref="List{string}"/> with duplicates removed, as well as strings with <see cref="string.IsNullOrWhiteSpace(string?)"/> == true.</para>
        /// </summary>
        /// <param name="items"></param>
        /// <returns></returns>
        public static List<string> DedupeStringList(List<string> items)
        {
            List<string> results = new List<string>();

            if (items.Count == 0)
                return results;

            foreach (string item in items)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                if (!results.Contains(item))
                {
                    results.Add(item);
                }
            }

            return results;
        }

        /// <summary>
        /// <para>Takes a <see cref="List{string?}"/> and returns a new <see cref="List{string}"/> with duplicates removed, as well as strings with <see cref="string.IsNullOrWhiteSpace(string?)"/> == true.</para>
        /// </summary>
        /// <param name="items"></param>
        /// <returns></returns>
        public static List<string> DedupeNullableStringList(List<string?> items)
        {
            List<string> results = new List<string>();

            if (items.Count == 0)
                return results;

            foreach (string? item in items)
            {
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }

                if (!results.Contains(item))
                {
                    results.Add(item);
                }
            }

            return results;
        }

        /// <summary>
        /// <para>Takes an input IPv4 or IPv6 IP address as a string. Returns the detected version (4 or 6) as well as a representation of the IP address in bytes.</para>
        /// <para>If the IP address failed to be parsed, returns (0, null).</para>
        /// </summary>
        /// <param name="ipAddress">The input IPv4 or IPv6 IP address to convert to bytes.</param>
        /// <returns></returns>
        public static (int ipVersion, byte[]? ipAddressBytes) IpAddressToBytes(string ipAddress)
        {
            if (IPAddress.TryParse(ipAddress, out IPAddress? parsedIpAddress))
            {
                int addressFamilyInt = 0;

                if (parsedIpAddress.AddressFamily == AddressFamily.InterNetwork)
                {
                    addressFamilyInt = 4;
                }
                else if (parsedIpAddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    addressFamilyInt = 6;
                }

                return (addressFamilyInt, parsedIpAddress.GetAddressBytes());
            }

            return (0, null);
        }

        /// <summary>
        /// <para>Returns a new <see cref="DateTime"/> which is rounded up to a given period.</para>
        /// <para>Written by redent84 of StackOverflow. https://stackoverflow.com/questions/7029353/how-can-i-round-up-the-time-to-the-nearest-x-minutes </para>
        /// </summary>
        /// <param name="dt">The input <see cref="DateTime"/> to round up.</param>
        /// <param name="d">A <see cref="TimeSpan"/> with the minutes to round to. Example use: TimeSpan.FromMinutes(15)</param>
        /// <returns></returns>
        public static DateTime DateTimeRoundUp(DateTime dt, TimeSpan d)
        {
            var modTicks = dt.Ticks % d.Ticks;
            var delta = modTicks != 0 ? d.Ticks - modTicks : 0;
            return new DateTime(dt.Ticks + delta, dt.Kind);
        }

        /// <summary>
        /// <para>Returns a new <see cref="DateTime"/> which is rounded down to a given period.</para>
        /// <para>Written by redent84 of StackOverflow. https://stackoverflow.com/questions/7029353/how-can-i-round-up-the-time-to-the-nearest-x-minutes </para>
        /// </summary>
        /// <param name="dt">The input <see cref="DateTime"/> to round down.</param>
        /// <param name="d">A <see cref="TimeSpan"/> with the minutes to round to. Example use: TimeSpan.FromMinutes(15)</param>
        /// <returns></returns>
        public static DateTime DateTimeRoundDown(DateTime dt, TimeSpan d)
        {
            var delta = dt.Ticks % d.Ticks;
            return new DateTime(dt.Ticks - delta, dt.Kind);
        }

        /// <summary>
        /// <para>Returns a new <see cref="DateTime"/> which is rounded to the nearest given period.</para>
        /// <para>Written by redent84 of StackOverflow. https://stackoverflow.com/questions/7029353/how-can-i-round-up-the-time-to-the-nearest-x-minutes </para>
        /// </summary>
        /// <param name="dt">The input <see cref="DateTime"/> to round to the nearest given period.</param>
        /// <param name="d">A <see cref="TimeSpan"/> with the minutes to round to. Example use: TimeSpan.FromMinutes(15)</param>
        /// <returns></returns>
        public static DateTime DateTimeRoundToNearest(DateTime dt, TimeSpan d)
        {
            var delta = dt.Ticks % d.Ticks;
            bool roundUp = delta > d.Ticks / 2;
            var offset = roundUp ? d.Ticks : 0;

            return new DateTime(dt.Ticks + offset - delta, dt.Kind);
        }

        /// <summary>
        /// <para>Returns a <see cref="DateTime"/> of the start of the week given by <paramref name="startOfWeek"/> relative to <see cref="DateTime.Today"/>.</para>
        /// <para>If today is currently the given day of week, then <see cref="DateTime.Today"/> is returned instead.</para>
        /// <para>Based on answer by "Compile This" of StackOverflow: https://stackoverflow.com/questions/38039/how-can-i-get-the-datetime-for-the-start-of-the-week/38064#38064</para>
        /// </summary>
        /// <param name="startOfWeek"></param>
        /// <returns></returns>
        public static DateTime StartOfWeek(DayOfWeek startOfWeek)
        {
            DateTime dt = DateTime.Today;
            int diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
            DateTime result = dt.AddDays(-1 * diff).Date;

            if (dt.Date == result)
            {
                return result.AddDays(-7);
            }
            else
            {
                return result;
            }
        }

        /// <summary>
        /// <para>Returns a <see cref="DateTime"/> of the start of the week given by <paramref name="startOfWeek"/> relative to the given <paramref name="dt"/>.</para>
        /// <para>If the given date is currently the given day of week, a <see cref="DateTime"/> for the same day is returned instead.</para>
        /// <para>Time component for the input date is ignored.</para>
        /// <para>Based on answer by "Compile This" of StackOverflow: https://stackoverflow.com/questions/38039/how-can-i-get-the-datetime-for-the-start-of-the-week/38064#38064</para>
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="startOfWeek"></param>
        /// <returns></returns>
        public static DateTime StartOfWeek(DateTime dt, DayOfWeek startOfWeek)
        {
            int diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
            DateTime result = dt.AddDays(-1 * diff).Date;

            if (dt.Date == result)
            {
                return result.AddDays(-7);
            }
            else
            {
                return result;
            }
        }

        /// <summary>
        /// <para>Returns a <see cref="DateTime"/> of the previous <paramref name="dayOfWeek"/> relative to <see cref="DateTime.Today"/>.</para>
        /// <para>If today is currently the given day of week, the date for the previous day (7 days ago) is returned instead.</para>
        /// <para>Based on answer by "Compile This" of StackOverflow: https://stackoverflow.com/questions/38039/how-can-i-get-the-datetime-for-the-start-of-the-week/38064#38064</para>
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="dayOfWeek"></param>
        /// <returns></returns>
        public static DateTime PreviousDayOfWeek(DayOfWeek dayOfWeek)
        {
            DateTime dt = DateTime.Today;
            int diff = (7 + (dt.DayOfWeek - dayOfWeek)) % 7;
            DateTime result = dt.AddDays(-1 * diff).Date;

            if (dt.Date == result)
            {
                return result.AddDays(-7);
            }
            else
            {
                return result;
            }
        }

        /// <summary>
        /// <para>Returns a <see cref="DateTime"/> of the previous <paramref name="dayOfWeek"/> relative to the given <paramref name="dt"/>.</para>
        /// <para>If the given date is currently the given day of week, the date for the previous day (7 days earlier) is returned instead.</para>
        /// <para>Time component for the input date is ignored.</para>
        /// <para>Based on answer by "Compile This" of StackOverflow: https://stackoverflow.com/questions/38039/how-can-i-get-the-datetime-for-the-start-of-the-week/38064#38064</para>
        /// </summary>
        /// <param name="dt"></param>
        /// <param name="dayOfWeek"></param>
        /// <returns></returns>
        public static DateTime PreviousDayOfWeek(DateTime dt, DayOfWeek dayOfWeek)
        {
            int diff = (7 + (dt.DayOfWeek - dayOfWeek)) % 7;
            DateTime result = dt.AddDays(-1 * diff).Date;

            if (dt.Date == result)
            {
                return result.AddDays(-7);
            }
            else
            {
                return result;
            }
        }

        /// <summary>
        /// <para>Returns the name of the method this function is called from.</para>
        /// <para>For example, calling this function from Program.Main() would return "Main".</para>
        /// </summary>
        /// <param name="callerName"></param>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string? GetCurrentMethodName([CallerMemberName] string? callerName = null)
        {
            return callerName;
        }

        /// <summary>
        /// Validates an email address. https://docs.microsoft.com/en-us/dotnet/standard/base-types/how-to-verify-that-strings-are-in-valid-email-format
        /// </summary>
        /// <param name="email"></param>
        /// <returns></returns>
        public static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            return EmailValidator.Validate(email);

            /*
            try
            {
                // Normalize the domain
                email = Regex.Replace(email, @"(@)(.+)$", DomainMapper,
                                      RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(200));

                // Examines the domain part of the email and normalizes it.
                string DomainMapper(Match match)
                {
                    // Use IdnMapping class to convert Unicode domain names.
                    IdnMapping idn = new IdnMapping();

                    // Pull out and process domain name (throws ArgumentException on invalid)
                    string domainName = idn.GetAscii(match.Groups[2].Value);

                    return match.Groups[1].Value + domainName;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }

            try
            {
                return Regex.IsMatch(email,
                    @"^(?("")("".+?(?<!\\)""@)|(([0-9a-z]((\.(?!\.))|[-!#\$%&'\*\+/=\?\^`\{\}\|~\w])*)(?<=[0-9a-z])@))" +
                    @"(?(\[)(\[(\d{1,3}\.){3}\d{1,3}\])|(([0-9a-z][-0-9a-z]*[0-9a-z]*\.)+[a-z0-9][\-a-z0-9]{0,22}[a-z0-9]))$",
                    RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
            */
        }

        /// <summary>
        /// Returns true is the specified <paramref name="timezone"/> is a valid IANA timezone, otherwise false.
        /// </summary>
        /// <param name="timezone">The input timezone to validate.</param>
        /// <returns></returns>
        public static bool IsValidTimezone(string timezone)
        {
            switch (timezone)
            {
                case "Africa/Abidjan":
                case "Africa/Accra":
                case "Africa/Addis_Ababa":
                case "Africa/Algiers":
                case "Africa/Asmara":
                case "Africa/Bamako":
                case "Africa/Bangui":
                case "Africa/Banjul":
                case "Africa/Bissau":
                case "Africa/Blantyre":
                case "Africa/Brazzaville":
                case "Africa/Bujumbura":
                case "Africa/Cairo":
                case "Africa/Casablanca":
                case "Africa/Ceuta":
                case "Africa/Conakry":
                case "Africa/Dakar":
                case "Africa/Dar_es_Salaam":
                case "Africa/Djibouti":
                case "Africa/Douala":
                case "Africa/El_Aaiun":
                case "Africa/Freetown":
                case "Africa/Gaborone":
                case "Africa/Harare":
                case "Africa/Johannesburg":
                case "Africa/Juba":
                case "Africa/Kampala":
                case "Africa/Khartoum":
                case "Africa/Kigali":
                case "Africa/Kinshasa":
                case "Africa/Lagos":
                case "Africa/Libreville":
                case "Africa/Lome":
                case "Africa/Luanda":
                case "Africa/Lubumbashi":
                case "Africa/Lusaka":
                case "Africa/Malabo":
                case "Africa/Maputo":
                case "Africa/Maseru":
                case "Africa/Mbabane":
                case "Africa/Mogadishu":
                case "Africa/Monrovia":
                case "Africa/Nairobi":
                case "Africa/Ndjamena":
                case "Africa/Niamey":
                case "Africa/Nouakchott":
                case "Africa/Ouagadougou":
                case "Africa/Porto-Novo":
                case "Africa/Sao_Tome":
                case "Africa/Tripoli":
                case "Africa/Tunis":
                case "Africa/Windhoek":
                case "America/Adak":
                case "America/Anchorage":
                case "America/Anguilla":
                case "America/Antigua":
                case "America/Araguaina":
                case "America/Argentina/Buenos_Aires":
                case "America/Argentina/Catamarca":
                case "America/Argentina/Cordoba":
                case "America/Argentina/Jujuy":
                case "America/Argentina/La_Rioja":
                case "America/Argentina/Mendoza":
                case "America/Argentina/Rio_Gallegos":
                case "America/Argentina/Salta":
                case "America/Argentina/San_Juan":
                case "America/Argentina/San_Luis":
                case "America/Argentina/Tucuman":
                case "America/Argentina/Ushuaia":
                case "America/Aruba":
                case "America/Asuncion":
                case "America/Atikokan":
                case "America/Bahia":
                case "America/Bahia_Banderas":
                case "America/Barbados":
                case "America/Belem":
                case "America/Belize":
                case "America/Blanc-Sablon":
                case "America/Boa_Vista":
                case "America/Bogota":
                case "America/Boise":
                case "America/Cambridge_Bay":
                case "America/Campo_Grande":
                case "America/Cancun":
                case "America/Caracas":
                case "America/Cayenne":
                case "America/Cayman":
                case "America/Chicago":
                case "America/Chihuahua":
                case "America/Ciudad_Juarez":
                case "America/Costa_Rica":
                case "America/Creston":
                case "America/Cuiaba":
                case "America/Curacao":
                case "America/Danmarkshavn":
                case "America/Dawson":
                case "America/Dawson_Creek":
                case "America/Denver":
                case "America/Detroit":
                case "America/Dominica":
                case "America/Edmonton":
                case "America/Eirunepe":
                case "America/El_Salvador":
                case "America/Fort_Nelson":
                case "America/Fortaleza":
                case "America/Glace_Bay":
                case "America/Goose_Bay":
                case "America/Grand_Turk":
                case "America/Grenada":
                case "America/Guadeloupe":
                case "America/Guatemala":
                case "America/Guayaquil":
                case "America/Guyana":
                case "America/Halifax":
                case "America/Havana":
                case "America/Hermosillo":
                case "America/Indiana/Indianapolis":
                case "America/Indiana/Knox":
                case "America/Indiana/Marengo":
                case "America/Indiana/Petersburg":
                case "America/Indiana/Tell_City":
                case "America/Indiana/Vevay":
                case "America/Indiana/Vincennes":
                case "America/Indiana/Winamac":
                case "America/Inuvik":
                case "America/Iqaluit":
                case "America/Jamaica":
                case "America/Juneau":
                case "America/Kentucky/Louisville":
                case "America/Kentucky/Monticello":
                case "America/Kralendijk":
                case "America/La_Paz":
                case "America/Lima":
                case "America/Los_Angeles":
                case "America/Lower_Princes":
                case "America/Maceio":
                case "America/Managua":
                case "America/Manaus":
                case "America/Marigot":
                case "America/Martinique":
                case "America/Matamoros":
                case "America/Mazatlan":
                case "America/Menominee":
                case "America/Merida":
                case "America/Metlakatla":
                case "America/Mexico_City":
                case "America/Miquelon":
                case "America/Moncton":
                case "America/Monterrey":
                case "America/Montevideo":
                case "America/Montserrat":
                case "America/Nassau":
                case "America/New_York":
                case "America/Nome":
                case "America/Noronha":
                case "America/North_Dakota/Beulah":
                case "America/North_Dakota/Center":
                case "America/North_Dakota/New_Salem":
                case "America/Nuuk":
                case "America/Ojinaga":
                case "America/Panama":
                case "America/Paramaribo":
                case "America/Phoenix":
                case "America/Port_of_Spain":
                case "America/Port-au-Prince":
                case "America/Porto_Velho":
                case "America/Puerto_Rico":
                case "America/Punta_Arenas":
                case "America/Rankin_Inlet":
                case "America/Recife":
                case "America/Regina":
                case "America/Resolute":
                case "America/Rio_Branco":
                case "America/Santarem":
                case "America/Santiago":
                case "America/Santo_Domingo":
                case "America/Sao_Paulo":
                case "America/Scoresbysund":
                case "America/Sitka":
                case "America/St_Barthelemy":
                case "America/St_Johns":
                case "America/St_Kitts":
                case "America/St_Lucia":
                case "America/St_Thomas":
                case "America/St_Vincent":
                case "America/Swift_Current":
                case "America/Tegucigalpa":
                case "America/Thule":
                case "America/Tijuana":
                case "America/Toronto":
                case "America/Tortola":
                case "America/Vancouver":
                case "America/Whitehorse":
                case "America/Winnipeg":
                case "America/Yakutat":
                case "America/Yellowknife":
                case "Antarctica/Casey":
                case "Antarctica/Davis":
                case "Antarctica/DumontDUrville":
                case "Antarctica/Macquarie":
                case "Antarctica/Mawson":
                case "Antarctica/McMurdo":
                case "Antarctica/Palmer":
                case "Antarctica/Rothera":
                case "Antarctica/Syowa":
                case "Antarctica/Troll":
                case "Antarctica/Vostok":
                case "Arctic/Longyearbyen":
                case "Asia/Aden":
                case "Asia/Almaty":
                case "Asia/Amman":
                case "Asia/Anadyr":
                case "Asia/Aqtau":
                case "Asia/Aqtobe":
                case "Asia/Ashgabat":
                case "Asia/Atyrau":
                case "Asia/Baghdad":
                case "Asia/Bahrain":
                case "Asia/Baku":
                case "Asia/Bangkok":
                case "Asia/Barnaul":
                case "Asia/Beirut":
                case "Asia/Bishkek":
                case "Asia/Brunei":
                case "Asia/Chita":
                case "Asia/Choibalsan":
                case "Asia/Colombo":
                case "Asia/Damascus":
                case "Asia/Dhaka":
                case "Asia/Dili":
                case "Asia/Dubai":
                case "Asia/Dushanbe":
                case "Asia/Famagusta":
                case "Asia/Gaza":
                case "Asia/Hebron":
                case "Asia/Ho_Chi_Minh":
                case "Asia/Hong_Kong":
                case "Asia/Hovd":
                case "Asia/Irkutsk":
                case "Asia/Jakarta":
                case "Asia/Jayapura":
                case "Asia/Jerusalem":
                case "Asia/Kabul":
                case "Asia/Kamchatka":
                case "Asia/Karachi":
                case "Asia/Kathmandu":
                case "Asia/Khandyga":
                case "Asia/Kolkata":
                case "Asia/Krasnoyarsk":
                case "Asia/Kuala_Lumpur":
                case "Asia/Kuching":
                case "Asia/Kuwait":
                case "Asia/Macau":
                case "Asia/Magadan":
                case "Asia/Makassar":
                case "Asia/Manila":
                case "Asia/Muscat":
                case "Asia/Nicosia":
                case "Asia/Novokuznetsk":
                case "Asia/Novosibirsk":
                case "Asia/Omsk":
                case "Asia/Oral":
                case "Asia/Phnom_Penh":
                case "Asia/Pontianak":
                case "Asia/Pyongyang":
                case "Asia/Qatar":
                case "Asia/Qostanay":
                case "Asia/Qyzylorda":
                case "Asia/Riyadh":
                case "Asia/Sakhalin":
                case "Asia/Samarkand":
                case "Asia/Seoul":
                case "Asia/Shanghai":
                case "Asia/Singapore":
                case "Asia/Srednekolymsk":
                case "Asia/Taipei":
                case "Asia/Tashkent":
                case "Asia/Tbilisi":
                case "Asia/Tehran":
                case "Asia/Thimphu":
                case "Asia/Tokyo":
                case "Asia/Tomsk":
                case "Asia/Ulaanbaatar":
                case "Asia/Urumqi":
                case "Asia/Ust-Nera":
                case "Asia/Vientiane":
                case "Asia/Vladivostok":
                case "Asia/Yakutsk":
                case "Asia/Yangon":
                case "Asia/Yekaterinburg":
                case "Asia/Yerevan":
                case "Atlantic/Azores":
                case "Atlantic/Bermuda":
                case "Atlantic/Canary":
                case "Atlantic/Cape_Verde":
                case "Atlantic/Faroe":
                case "Atlantic/Madeira":
                case "Atlantic/Reykjavik":
                case "Atlantic/South_Georgia":
                case "Atlantic/St_Helena":
                case "Atlantic/Stanley":
                case "Australia/Adelaide":
                case "Australia/Brisbane":
                case "Australia/Broken_Hill":
                case "Australia/Darwin":
                case "Australia/Eucla":
                case "Australia/Hobart":
                case "Australia/Lindeman":
                case "Australia/Lord_Howe":
                case "Australia/Melbourne":
                case "Australia/Perth":
                case "Australia/Sydney":
                case "Europe/Amsterdam":
                case "Europe/Andorra":
                case "Europe/Astrakhan":
                case "Europe/Athens":
                case "Europe/Belgrade":
                case "Europe/Berlin":
                case "Europe/Bratislava":
                case "Europe/Brussels":
                case "Europe/Bucharest":
                case "Europe/Budapest":
                case "Europe/Busingen":
                case "Europe/Chisinau":
                case "Europe/Copenhagen":
                case "Europe/Dublin":
                case "Europe/Gibraltar":
                case "Europe/Guernsey":
                case "Europe/Helsinki":
                case "Europe/Isle_of_Man":
                case "Europe/Istanbul":
                case "Europe/Jersey":
                case "Europe/Kaliningrad":
                case "Europe/Kirov":
                case "Europe/Kyiv":
                case "Europe/Lisbon":
                case "Europe/Ljubljana":
                case "Europe/London":
                case "Europe/Luxembourg":
                case "Europe/Madrid":
                case "Europe/Malta":
                case "Europe/Mariehamn":
                case "Europe/Minsk":
                case "Europe/Monaco":
                case "Europe/Moscow":
                case "Europe/Oslo":
                case "Europe/Paris":
                case "Europe/Podgorica":
                case "Europe/Prague":
                case "Europe/Riga":
                case "Europe/Rome":
                case "Europe/Samara":
                case "Europe/San_Marino":
                case "Europe/Sarajevo":
                case "Europe/Saratov":
                case "Europe/Simferopol":
                case "Europe/Skopje":
                case "Europe/Sofia":
                case "Europe/Stockholm":
                case "Europe/Tallinn":
                case "Europe/Tirane":
                case "Europe/Ulyanovsk":
                case "Europe/Vaduz":
                case "Europe/Vatican":
                case "Europe/Vienna":
                case "Europe/Vilnius":
                case "Europe/Volgograd":
                case "Europe/Warsaw":
                case "Europe/Zagreb":
                case "Europe/Zurich":
                case "Indian/Antananarivo":
                case "Indian/Chagos":
                case "Indian/Christmas":
                case "Indian/Cocos":
                case "Indian/Comoro":
                case "Indian/Kerguelen":
                case "Indian/Mahe":
                case "Indian/Maldives":
                case "Indian/Mauritius":
                case "Indian/Mayotte":
                case "Indian/Reunion":
                case "Pacific/Apia":
                case "Pacific/Auckland":
                case "Pacific/Bougainville":
                case "Pacific/Chatham":
                case "Pacific/Chuuk":
                case "Pacific/Easter":
                case "Pacific/Efate":
                case "Pacific/Fakaofo":
                case "Pacific/Fiji":
                case "Pacific/Funafuti":
                case "Pacific/Galapagos":
                case "Pacific/Gambier":
                case "Pacific/Guadalcanal":
                case "Pacific/Guam":
                case "Pacific/Honolulu":
                case "Pacific/Kanton":
                case "Pacific/Kiritimati":
                case "Pacific/Kosrae":
                case "Pacific/Kwajalein":
                case "Pacific/Majuro":
                case "Pacific/Marquesas":
                case "Pacific/Midway":
                case "Pacific/Nauru":
                case "Pacific/Niue":
                case "Pacific/Norfolk":
                case "Pacific/Noumea":
                case "Pacific/Pago_Pago":
                case "Pacific/Palau":
                case "Pacific/Pitcairn":
                case "Pacific/Pohnpei":
                case "Pacific/Port_Moresby":
                case "Pacific/Rarotonga":
                case "Pacific/Saipan":
                case "Pacific/Tahiti":
                case "Pacific/Tarawa":
                case "Pacific/Tongatapu":
                case "Pacific/Wake":
                case "Pacific/Wallis":
                    return true;
            }

            return false;
        }

        /// <summary>
        /// <para>Parses an email address and returns the host part. Returns null if the email address could not be parsed.</para>
        /// <para>e.g. "User@Google.com" returns "Google.com".</para>
        /// </summary>
        /// <param name="email"></param>
        /// <returns></returns>
        public static string? GetDomainFromEmailAddress(string email)
        {
            if (MailAddress.TryCreate(email, out MailAddress? mailAddress))
            {
                return mailAddress!.Host;
            }

            return null;
        }

        /// <summary>
        /// <para>Parses an email address and returns the host part converted to lowercase with Punycode encoding. Returns null if the email address could not be parsed.</para>
        /// <para>See: https://en.wikipedia.org/wiki/Punycode</para>
        /// <para>e.g. "user@GOOGLE.COM" returns "google.com".</para>
        /// <para>e.g. "user@пример.рф" returns "xn--e1afmkfd.xn--p1ai".</para>
        /// </summary>
        /// <param name="email"></param>
        /// <returns></returns>
        public static string? GetNormalizedDomainFromEmailAddress(string email)
        {
            try
            {
                if (MailAddress.TryCreate(email, out MailAddress? mailAddress))
                {
                    string domainName = mailAddress!.Host;

                    // Use IdnMapping class to convert Unicode domain names.
                    IdnMapping idn = new IdnMapping();

                    // Pull out and process domain name (throws ArgumentException on invalid)
                    return idn.GetAscii(domainName).ToLowerInvariant();
                }

                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// <para>Takes an email address, and returns the same email address converted to lowercase with the host part converted to Punycode encoding. Returns null if the email is invalid.</para>
        /// <para>See: https://en.wikipedia.org/wiki/Punycode</para>
        /// <para>e.g. "user@GOOGLE.COM" returns "user@google.com".</para>
        /// <para>e.g. "user@пример.рф" returns "user@xn--e1afmkfd.xn--p1ai".</para>
        /// </summary>
        /// <param name="email">The input email address to convert.</param>
        /// <returns></returns>
        public static string? ValidateAndNormalizeEmailAddress(string email)
        {
            try
            {
                // Normalize the domain
                email = Regex.Replace(email, @"(@)(.+)$", DomainMapper,
                                      RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(200));

                // Examines the domain part of the email and normalizes it.
                string DomainMapper(Match match)
                {
                    // Use IdnMapping class to convert Unicode domain names.
                    IdnMapping idn = new IdnMapping();

                    // Pull out and process domain name (throws ArgumentException on invalid)
                    string domainName = idn.GetAscii(match.Groups[2].Value);

                    return match.Groups[1].Value + domainName;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }

            try
            {
                email = email.ToLowerInvariant();

                if (Regex.IsMatch(email,
                    @"^(?("")("".+?(?<!\\)""@)|(([0-9a-z]((\.(?!\.))|[-!#\$%&'\*\+/=\?\^`\{\}\|~\w])*)(?<=[0-9a-z])@))" +
                    @"(?(\[)(\[(\d{1,3}\.){3}\d{1,3}\])|(([0-9a-z][-0-9a-z]*[0-9a-z]*\.)+[a-z0-9][\-a-z0-9]{0,22}[a-z0-9]))$",
                    RegexOptions.None, TimeSpan.FromMilliseconds(250)))
                {
                    return email;
                }

                return null;
            }
            catch (RegexMatchTimeoutException)
            {
                return null;
            }
        }

        /// <summary>
        /// <para>Takes a <see cref="DynamicParameters"/> and returns a string that creates the same variables which can be pasted into SQL Server Management Studio.</para>
        /// <para>Based on solution written by Vahid Farahmandian on StackOverflow: https://stackoverflow.com/questions/35179812/can-a-get-dbtype-from-dapper-dynamicparameters/59553978#59553978 </para>
        /// </summary>
        /// <param name="parameters"></param>
        /// <returns></returns>
        public static string DynamicParametersToSql(DynamicParameters parameters)
        {
#pragma warning disable S3011
            FieldInfo? fieldInfo = parameters.GetType().GetField("parameters", BindingFlags.NonPublic | BindingFlags.Instance);
#pragma warning restore S3011

            if (fieldInfo is null)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder();
            IDictionary? paramDict = (IDictionary?)fieldInfo.GetValue(parameters);

            if (paramDict is null)
            {
                return string.Empty;
            }

            foreach (DictionaryEntry dictionaryEntry in paramDict)
            {
                Type? paramInfo = dictionaryEntry.Value?.GetType();

                if (paramInfo is null)
                {
                    continue;
                }

                string? name = (string?)paramInfo.GetProperty("Name")?.GetValue(dictionaryEntry.Value);
                object? value = paramInfo.GetProperty("Value")?.GetValue(dictionaryEntry.Value);
                int? size = (int?)paramInfo.GetProperty("Size")?.GetValue(dictionaryEntry.Value);
                int? precision = (int?)paramInfo.GetProperty("Precision")?.GetValue(dictionaryEntry.Value);
                int? scale = (int?)paramInfo.GetProperty("Scale")?.GetValue(dictionaryEntry.Value);
                DbType? dbType = (DbType?)paramInfo.GetProperty("DbType")?.GetValue(dictionaryEntry.Value);

                string sqlType;
                bool isQuotedValue = false;

                switch (dbType)
                {
                    case DbType.AnsiString:
                        size ??= 8000; // 8000 bytes

                        sqlType = $"varchar({(size == -1 || size == int.MaxValue ? "max" : size)})";
                        isQuotedValue = true;
                        break;
                    case DbType.String:
                        size ??= 4000; // wide chars = 8000 bytes

                        sqlType = $"nvarchar({(size == -1 || size == int.MaxValue ? "max" : size)})";
                        isQuotedValue = true;
                        break;
                    case DbType.AnsiStringFixedLength:
                        size ??= 8000; // 8000 bytes

                        sqlType = $"char({(size == -1 || size == int.MaxValue ? "max" : size)})";
                        isQuotedValue = true;
                        break;
                    case DbType.StringFixedLength:
                        size ??= 4000; // wide chars = 8000 bytes

                        sqlType = $"nchar({(size == -1 || size == int.MaxValue ? "max" : size)})";
                        isQuotedValue = true;
                        break;
                    case DbType.Guid:
                        sqlType = $"uniqueidentifier";
                        isQuotedValue = true;
                        break;
                    case DbType.Int16:
                        sqlType = $"smallint";
                        break;
                    case DbType.Int32:
                        sqlType = $"int";
                        break;
                    case DbType.Int64:
                        sqlType = $"bigint";
                        break;
                    case DbType.Byte:
                        sqlType = $"tinyint";
                        break;
                    case DbType.Binary:
                        sqlType = $"varbinary({size})";
                        break;
                    case DbType.Currency:
                        sqlType = $"money";
                        break;
                    case DbType.Boolean:
                        sqlType = $"bit";
                        break;
                    case DbType.Date:
                        sqlType = $"date";
                        isQuotedValue = true;
                        break;
                    case DbType.DateTime:
                        sqlType = $"datetime";
                        isQuotedValue = true;
                        break;
                    case DbType.DateTime2:
                        sqlType = $"datetime2({size})";
                        isQuotedValue = true;
                        break;
                    case DbType.DateTimeOffset:
                        sqlType = $"datetimeoffset({size})";
                        isQuotedValue = true;
                        break;
                    case DbType.Decimal:
                        sqlType = $"decimal({precision}, {scale})";
                        break;
                    case DbType.Single:
                        sqlType = $"float";
                        break;
                    case DbType.Time:
                        sqlType = $"time({size})";
                        isQuotedValue = true;
                        break;
                    default:
                        sqlType = "?";
                        break;
                }

                sb.Append($"declare {name} {sqlType} = ");

                if (value is null)
                {
                    sb.Append("null");
                }
                else
                {
                    if (isQuotedValue)
                    {
                        sb.Append('\'');
                    }

                    switch (dbType)
                    {
                        case DbType.Boolean:
                            if ((bool)value)
                            {
                                sb.Append('1');
                            }
                            else
                            {
                                sb.Append('0');
                            }
                            break;
                        case DbType.Byte:
                            sb.Append((int)value);
                            break;
                        case DbType.Date:
                            sb.Append(((DateTime)value).ToString("yyyy-MM-dd"));
                            break;
                        case DbType.DateTime:
                        case DbType.DateTime2:
                            sb.Append(((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss.fffffff"));
                            break;
                        case DbType.DateTimeOffset:
                            sb.Append(((DateTimeOffset)value).ToString("yyyy-MM-dd HH:mm:ss.fffffff zzz"));
                            break;
                        case DbType.Time:
                            sb.Append(((DateTime)value).ToString("HH:mm:ss.fffffff"));
                            break;
                        case DbType.Binary:
                            sb.Append("0x");
                            sb.Append(Convert.ToHexString((byte[])value));
                            break;
                        case DbType.Int16:
                            sb.Append((short)value);
                            break;
                        case DbType.Int32:
                            sb.Append((int)value);
                            break;
                        case DbType.Int64:
                            sb.Append((long)value);
                            break;
                        case DbType.UInt16:
                            sb.Append((ushort)value);
                            break;
                        case DbType.UInt32:
                            sb.Append((uint)value);
                            break;
                        case DbType.UInt64:
                            sb.Append((ulong)value);
                            break;
                        default:
                            if (isQuotedValue)
                            {
                                sb.Append(value?.ToString()?.Replace("'", "''"));
                            }
                            else
                            {
                                sb.Append(value);
                            }
                            break;
                    }

                    if (isQuotedValue)
                    {
                        sb.Append('\'');
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }
    }

    public sealed class MailAttachmentData
    {
        public string? FileName { get; set; }
        public string? DataBase64 { get; set; }
        public string? MimeType { get; set; }
    }

    public sealed class GuidPair
    {
        public Guid OldId { get; set; }
        public Guid NewId { get; set; }

        public GuidPair() { }

        public GuidPair(Guid oldId, Guid newId)
        {
            OldId = oldId;
            NewId = newId;
        }
    }

    public sealed class TableGuidPair
    {
        public required string TableName { get; set; }
        public required Guid OldId { get; set; }
        public required Guid NewId { get; set; }
    }

    public sealed class TableColumnGuidPair
    {
        public required string TableName { get; set; }
        public required string ColumnName { get; set; }
        public required Guid OldId { get; set; }
        public required Guid NewId { get; set; }
    }

    public sealed class UpdateGuidsUsingGuidMapDestinationColumnOptions
    {
        public required string TableName { get; set; }
        public required string ColumnName { get; set; }
        /// <summary>
        /// Should be either <see cref="DbType.Guid"/>, <see cref="DbType.String"/> or <see cref="DbType.AnsiString"/>.
        /// </summary>
        public required DbType DbType { get; set; }
        /// <summary>
        /// Must be specified if <see cref="DbType"/> is <see cref="DbType.String"/> or <see cref="DbType.AnsiString"/>.
        /// </summary>
        public int? Size { get; set; }
    }
}
