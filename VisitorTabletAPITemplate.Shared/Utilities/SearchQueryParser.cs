using System.Text;
using System.Text.RegularExpressions;

namespace VisitorTabletAPITemplate.Utilities
{
    public sealed class SearchTermSqlInfo
    {
        public string? SqlTableName { get; set; }
        public required string SqlColumnName { get; set; }
        public required SearchTerm SearchTerm { get; set; }
    }

    public sealed class SearchTermSqlInfoMultiple
    {
        public required List<SearchTermSqlInfoTableColumnPair> TableColumnPairs { get; set; }
        public required SearchTerm SearchTerm { get; set; }
    }

    public sealed class SearchTermSqlInfoTableColumnPair
    {
        public required string SqlTableName { get; set; }
        public required string SqlColumnName { get; set; }
    }

    public sealed class SearchTerm
    {
        public string Field { get; set; }
        private string? _term;
        public string Term
        {
            get
            {
                return _term!;
            }
            set
            {
                _term = value;
                EscapedTerm = SqlGetEscapedLikeString(value);
            }
        }
        public string EscapedTerm { get; private set; }

        public SearchTerm(string field, string term)
        {
            Field = field;
            Term = term;
            EscapedTerm = SqlGetEscapedLikeString(term);
        }

        /// <summary>
        /// <para>Escapes a string so that it is safe to use in a LIKE query.</para>
        /// <para>Replaces the following characters: <paramref name="escapeCharacter"/>, %, _ and [, with <paramref name="escapeCharacter"/> then the character.</para>
        /// <para>Replaces single quotes with two single quotes.</para>
        /// <para>Returns modified string.</para>
        /// </summary>
        /// <param name="str">Input string to modify.</param>
        string SqlGetEscapedLikeString(string str, char escapeCharacter = '!')
        {
            if (str == "" || str == null)
                return "";

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
    }

    public static class SearchQueryParser
    {
        public static string? BuildSqlFilterString(List<int> list, string sqlColumnName)
        {
            if (list.Count == 0)
            {
                return null;
            }

            return $" and {GetQuoteName(sqlColumnName)} in ({string.Join(",", list)})";
        }

        public static string? BuildSqlFilterString(List<int> list, string? sqlTableName, string sqlColumnName)
        {
            if (list.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(sqlTableName))
            {
                return $" and {GetQuoteName(sqlTableName)}.{GetQuoteName(sqlColumnName)} in ({string.Join(",", list)})";
            }

            return $" and {GetQuoteName(sqlColumnName)} in ({string.Join(",", list)})";
        }

        public static string? BuildSqlFilterString<T>(List<T> list, string sqlColumnName)
        {
            if (list.Count == 0)
            {
                return null;
            }

            return $" and {GetQuoteName(sqlColumnName)} in ({string.Join(",", list)})";
        }

        public static string? BuildSqlFilterString<T>(List<T> list, string? sqlTableName, string sqlColumnName)
        {
            if (list.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(sqlTableName))
            {
                return $" and {GetQuoteName(sqlTableName)}.{GetQuoteName(sqlColumnName)} in ({SqlBuildEscapedInString(list)})";
            }

            return $" and {GetQuoteName(sqlColumnName)} in ({SqlBuildEscapedInString(list)})";
        }

        public static string? BuildSqlFilterString(int value, string sqlColumnName)
        {
            return $" and {GetQuoteName(sqlColumnName)} = {value}";
        }

        public static string? BuildSqlFilterString(int value, string? sqlTableName, string sqlColumnName)
        {
            if (!string.IsNullOrEmpty(sqlTableName))
            {
                return $" and {GetQuoteName(sqlTableName)}.{GetQuoteName(sqlColumnName)} = {value}";
            }

            return $" and {GetQuoteName(sqlColumnName)} = {value}";
        }

        public static string? BuildSqlFilterString<T>(T value, string sqlColumnName)
        {
            if (value is null)
            {
                return null;
            }

            return $" and {GetQuoteName(sqlColumnName)} = {value}";
        }

        public static string? BuildSqlFilterString<T>(T value, string? sqlTableName, string sqlColumnName)
        {
            if (value is null)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(sqlTableName))
            {
                return $" and {GetQuoteName(sqlTableName)}.{GetQuoteName(sqlColumnName)} = {value}";
            }

            return $" and {GetQuoteName(sqlColumnName)} = {value}";
        }

        public static string BuildSqlString(SearchTerm searchTerm, string sqlColumnName)
        {
            return $" and {GetQuoteName(sqlColumnName)} like N'%{searchTerm.EscapedTerm}%' escape N'!'";
        }

        public static string? BuildSqlString(SearchTerm searchTerm, List<SearchTermSqlInfoTableColumnPair> tableColumnPairs)
        {
            if (tableColumnPairs.Count == 0)
            {
                return null;
            }

            string sqlString = @"
and
(
";

            bool first = true;

            foreach (SearchTermSqlInfoTableColumnPair pair in tableColumnPairs)
            {
                if (string.IsNullOrEmpty(pair.SqlColumnName))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(pair.SqlTableName))
                {
                    sqlString += $" {(!first ? "or " : "")}{GetQuoteName(pair.SqlTableName)}.{GetQuoteName(pair.SqlColumnName)} like N'%{searchTerm.EscapedTerm}%' escape N'!'";
                }
                else
                {
                    sqlString += $" {(!first ? "or " : "")}{GetQuoteName(pair.SqlColumnName)} like N'%{searchTerm.EscapedTerm}%' escape N'!'";
                }

                first = false;
            }

            sqlString += @"
)
";

            return sqlString;
        }

        public static string BuildSqlString(SearchTerm searchTerm, string? sqlTableName, string sqlColumnName)
        {
            if (!string.IsNullOrEmpty(sqlTableName))
            {
                return $" and {GetQuoteName(sqlTableName)}.{GetQuoteName(sqlColumnName)} like N'%{searchTerm.EscapedTerm}%' escape N'!'";
            }

            return $" and {GetQuoteName(sqlColumnName)} like N'%{searchTerm.EscapedTerm}%' escape N'!'";
        }

        public static string BuildSqlString(SearchTerm searchTerm, SearchTermSqlInfo searchTermSqlInfo)
        {
            if (!string.IsNullOrEmpty(searchTermSqlInfo.SqlTableName))
            {
                return $" and {GetQuoteName(searchTermSqlInfo.SqlTableName)}.{GetQuoteName(searchTermSqlInfo.SqlColumnName)} like N'%{searchTerm.EscapedTerm}%' escape N'!'";

            }

            return $" and {GetQuoteName(searchTermSqlInfo.SqlColumnName)} like N'%{searchTerm.EscapedTerm}%' escape N'!'";
        }

        public static string? BuildSqlString(IEnumerable<SearchTermSqlInfo> searchTermSqlInfos)
        {
            string? sqlString = null;
            bool first = true;

            foreach (SearchTermSqlInfo searchTermSqlInfo in searchTermSqlInfos)
            {
                if (string.IsNullOrEmpty(searchTermSqlInfo.SqlColumnName))
                {
                    continue;
                }

                if (first)
                {
                    sqlString = @"
and
(
";
                }

                if (!string.IsNullOrEmpty(searchTermSqlInfo.SqlTableName))
                {
                    sqlString += $"    {(!first ? "or " : "")}{GetQuoteName(searchTermSqlInfo.SqlTableName)}.{GetQuoteName(searchTermSqlInfo.SqlColumnName)} like N'%{searchTermSqlInfo.SearchTerm.EscapedTerm}%' escape N'!'\n";
                }
                else
                {
                    sqlString += $"    {(!first ? "or " : "")}{GetQuoteName(searchTermSqlInfo.SqlColumnName)} like N'%{searchTermSqlInfo.SearchTerm.EscapedTerm}%' escape N'!'\n";
                }

                first = false;
            }

            if (first) return null;

            sqlString += @"
)
";

            return sqlString;
        }

        public static string BuildSqlString(IEnumerable<SearchTermSqlInfoMultiple> searchTermSqlInfosMultiple)
        {
            string? sqlString = null;
            bool first = true;
            bool innerFirst;

            sqlString += @"
and
(
";

            foreach (SearchTermSqlInfoMultiple searchTermSqlInfo in searchTermSqlInfosMultiple)
            {
                sqlString += @$"
    {(!first ? "or\n    (" : "(")}";

                innerFirst = true;

                foreach (SearchTermSqlInfoTableColumnPair pair in searchTermSqlInfo.TableColumnPairs)
                {
                    if (string.IsNullOrEmpty(pair.SqlColumnName))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(pair.SqlTableName))
                    {
                        sqlString += $" {(!innerFirst ? "or " : "")}{GetQuoteName(pair.SqlTableName)}.{GetQuoteName(pair.SqlColumnName)} like N'%{searchTermSqlInfo.SearchTerm.EscapedTerm}%' escape N'!'";
                    }
                    else
                    {
                        sqlString += $" {(!innerFirst ? "or " : "")}{GetQuoteName(pair.SqlColumnName)} like N'%{searchTermSqlInfo.SearchTerm.EscapedTerm}%' escape N'!'";
                    }

                    innerFirst = false;
                }

                sqlString += @")";
                first = false;
            }

            sqlString += @"
)
";

            return sqlString;
        }

        /// <summary>
        /// <para>Escapes a column/table name so that it is safe to use.</para>
        /// <para>Adds [ and ] to the start and end and replaces ] with ]].</para>
        /// <para>Keeps only characters between chr(32) to chr(127).</para>
        /// <para>Returns modified string.</para>
        /// </summary>
        /// <param name="str">Input string to modify.</param>
        static string GetQuoteName(string str)
        {
            if (str == "" || str == null)
                return "";

            str = Regex.Replace(str, @"[^\u0020-\u007F]", string.Empty);
            str = "[" + str.Replace("]", "]]") + "]";

            if (str.Length > 130)
                str = str.Substring(0, 130);

            return str;
        }

        /// <summary>
        /// <para>Takes an <see cref="IEnumerable{string}"/>, calls <see cref="SqlGetEscapedString(string)"/> on each, and returns a string containing the list with each item in quotes, each separated by a comma and newline.</para>
        /// <para>Intended to be used when building the "where x in ( y, z )" part of an SQL query.</para>
        /// <para>e.g. string[] { "One", "Twos", "Three's", "Four" }</para>
        /// <para>would return 'One',\n'Twos',\n'Three''s'\n,'Four'</para>
        /// </summary>
        /// <param name="items">List of strings to use to create the output.</param>
        /// <returns></returns>
        static string SqlBuildEscapedInString<T>(IEnumerable<T> items)
        {
            StringBuilder sb = new StringBuilder();

            using (IEnumerator<T> i = items.GetEnumerator())
            {
                i.MoveNext();

                while (true)
                {
                    if (i.Current == null)
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
        /// <para>Escapes a string so that it is safe to use in a query.</para>
        /// <para>Removes control characters except for tab, line feed and carriage return.</para>
        /// <para>Replaces single quotes with two single quotes.</para>
        /// <para>Returns modified string.</para>
        /// </summary>
        /// <param name="str">Input string to modify.</param>
        public static string? SqlGetEscapedString(string? str)
        {
            if (str == "" || str == null)
                return str;

            // Remove control characters
            // https://en.wikipedia.org/wiki/Control_character#:~:text=In%20Unicode%2C%20%22Control%2Dcharacters,in%20General%20Category%20%22Cf%22.
            str = Regex.Replace(str, @"[\u0000-\u0008\u000b\u000c\u000e-\u001F\u007F\u0080-\u009F]", string.Empty);
            return str.Replace("'", "''");
        }

        /*
        Reads an input search string and parses it into field:term pairs.

        How it is supposed to work: (see known bugs below)
        - Words separated by spaces are split into multiple terms.
          e.g. input: abc def 123
               result:
                [
                  {
                      "field": "",
                      "term": "abc"
                  },
                  {
                      "field": "",
                      "term": "def"
                  },
                  {
                      "field": "",
                      "term": "123"
                  }
                ]
        - Quotes can be used to put multiple words into one search term, creating a phrase
          e.g. input: "example phrase" abc
               result:
                [
                  {
                      "field": "",
                      "term": "example phrase"
                  },
                  {
                      "field": "",
                      "term": "abc"
                  }
                ]
        - Strings without any spaces, but have double quotes, will be split to separate terms
          e.g. input: abc"def"123
               result:
                [
                  {
                      "field": "",
                      "term": "abc"
                  },
                  {
                      "field": "",
                      "term": "def"
                  },
                  {
                      "field": "",
                      "term": "123"
                  }
                ]
        - field:term input can be used to specify terms for a specific field
          e.g. input: name:cool email:coolemail
               result:
                [
                  {
                      "field": "name",
                      "term": "cool"
                  },
                  {
                      "field": "email",
                      "term": "coolemail"
                  }
                ]
        - field:term can be used with double quotes to make phrases
          e.g. input: name:"example name" email:coolemail
               result:
                [
                  {
                      "field": "name",
                      "term": "example name"
                  },
                  {
                      "field": "email",
                      "term": "coolemail"
                  }
                ]
        - if a double quote is not closed, then the remainder of the string is considered to be the phrase
          e.g. input: cool "story bro
               result:
                [
                  {
                      "field": "",
                      "term": "cool"
                  },
                  {
                      "field": "",
                      "term": "story bro"
                  }
                ]
        - all terms are trimmed (spaces removed from start and end) and multiple spaces are replaced with one space
          e.g. input: cool    "   story   bro "
               result:
                [
                  {
                      "field": "",
                      "term": "cool"
                  },
                  {
                      "field": "",
                      "term": "story bro"
                  }
                ]
        - double quotes within a phrase can be escaped with two double quotes
          e.g. input: "hello ""young"" fellow"
               result:
                [
                  {
                      "field": "",
                      "term": "hello \"young\" fellow"
                  }
                ]
        - fields without a term are ignored
          e.g. input: name: email: abc
                [
                  {
                      "field": "",
                      "term": "abc"
                  }
                ]

        **** Known bugs: ****
        - Quotes currently not supported in field names
          e.g. input: "name":"example name"
               expected result: (single field and term parsed)
                [
                  {
                      "field": "name",
                      "term": "example name"
                  }
                ]
               actual result: (incorrectly parsed as 2 terms without field name)
                [
                  {
                      "field": "",
                      "term": "name"
                  },
                  {
                      "field": "",
                      "term": "example name"
                  }
                ]
        - For terms not inside quotes, putting 3 or more double quotes in a row isn't parsed correctly
          (result has too many or too little double quotes)
          e.g. input: abc""""123 def
               expected result: (see comments)
                [
                  {
                      "field": "",
                      "term": "abc\"\"123" // 4 double quotes escaped and replaced down to 2 actual double quotes, term includes 123
                  },
                  {
                      "field": "",
                      "term": "def"
                  }
                ]
               actual result: (see comments)
                [
                  {
                      "field": "",
                      "term": "abc\"\"\"" // result has 3 quotes
                  },
                  {
                      "field": "",
                      "term": "123 def" // 123 was moved into next term
                  }
                ]
        - Field:Term with empty quoted term is now broken (fixing above bugs will probably will fix this)
          e.g. input: name:"" abc
               expected result: (name is ignored as it is a field with no term, abc is separate term)
                [
                  {
                      "field": "",
                      "term": "abc"
                  }
                ]
               actual result: (string recognised as field and term with escaped quote)
                [
                  {
                      "field": "name",
                      "term": "\" abc"
                  }
                ]
        - Another quote issue (related to above):
          e.g. input: abc ""def"
               expected result:
                [
                  {
                      "field": "",
                      "term": "abc"
                  },
                  {
                      "field": "",
                      "term": "\"def" // should be only one double quote
                  }
                ]
               actual result:
                [
                  {
                      "field": "",
                      "term": "abc"
                  },
                  {
                      "field": "",
                      "term": "\"\"def" // two double quotes are appearing
                  }
                ]
        */
        public static List<SearchTerm> SplitTerms(string? value)
        {
            List<SearchTerm> results = new List<SearchTerm>();

            // Trim and replace tab/enter with space
            value = value?.Replace("\u0009", " ").Replace("\u000a", " ").Replace("\u000d", " ").Trim();

            if (string.IsNullOrEmpty(value))
            {
                return results;
            }

            // Remove double spaces
            value = Regex.Replace(value, @"[ ]+", " ");

            string currentResult = "";
            string currentKey = "";
            int startIndex = 0;
            bool insideQuotes = false;
            bool haveKey = false;

            while (startIndex < value.Length)
            {
                int endIndex = value.IndexOfAny(new char[] { ' ', '"', ':' }, startIndex);

                // If at the end of the string, take the remainder of the string
                if (endIndex == -1)
                {
                    endIndex = value.Length - 1;
                }

                if (value[endIndex] == ':' && !insideQuotes && !haveKey)
                {
                    currentResult += value.Substring(startIndex, (endIndex - startIndex) + 1);

                    if (currentResult == ":")
                    {
                        // Found ':' on it's own, so ignore it
                        currentResult = "";
                        startIndex = endIndex + 1;
                    }
                    else
                    {
                        // Add to collection - found ':' not in quotes
                        currentKey = currentResult.Substring(0, currentResult.Length - 1); // don't put ':' in the key
                        haveKey = true;
                        startIndex = endIndex + 1;
                        currentResult = "";
                    }
                }
                else if (value[endIndex] == '"')
                {
                    if (!insideQuotes)
                    {
                        if (endIndex < value.Length - 1 && value[endIndex + 1] == '"')
                        {
                            if (endIndex + 1 == value.Length - 1)
                            {
                                // Found double quote at end of string, so actually quote not correctly terminated. Just take the rest of the string as the last term.
                                insideQuotes = false;
                                currentResult += value.Substring(startIndex);
                                currentResult = currentResult.Trim().Replace("\"\"", "\""); // trim spaces within quotes and convert escaped '""' back to a single '"'

                                if (!string.IsNullOrEmpty(currentResult))
                                {
                                    if (haveKey)
                                    {
                                        results.Add(new SearchTerm(currentKey.ToLowerInvariant(), currentResult));
                                        haveKey = false;
                                    }
                                    else
                                    {
                                        results.Add(new SearchTerm("", currentResult));
                                    }
                                }

                                startIndex = endIndex + 1;
                                currentResult = "";
                            }
                            else
                            {
                                ++endIndex;
                                currentResult += value.Substring(startIndex, (endIndex - startIndex) + 2); // +2 == include 2x double quotes in result
                                startIndex = endIndex + 2; // +2 == skip 2x double quotes

                                // End of string reached so add to results
                                if (startIndex == value.Length)
                                {
                                    currentResult = currentResult.Trim().Replace("\"\"", "\""); // trim spaces within quotes and convert escaped '""' back to a single '"'

                                    if (!string.IsNullOrEmpty(currentResult))
                                    {
                                        if (haveKey)
                                        {
                                            results.Add(new SearchTerm(currentKey.ToLowerInvariant(), currentResult));
                                            haveKey = false;
                                        }
                                        else
                                        {
                                            results.Add(new SearchTerm("", currentResult));
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            insideQuotes = true;
                            currentResult += value.Substring(startIndex, (endIndex - startIndex));

                            // Found quote in the middle of a string, take what we have so far as a term and start looking for next string
                            if (!string.IsNullOrEmpty(currentResult))
                            {
                                if (haveKey)
                                {
                                    results.Add(new SearchTerm(currentKey.ToLowerInvariant(), currentResult));
                                    haveKey = false;
                                }
                                else
                                {
                                    results.Add(new SearchTerm("", currentResult));
                                }
                            }

                            startIndex = endIndex + 1;
                            currentResult = "";
                        }
                    }
                    else if (endIndex < value.Length - 1 && value[endIndex + 1] == '"')
                    {
                        if (endIndex + 1 == value.Length - 1)
                        {
                            // Found double quote at end of string, so actually quote not correctly terminated. Just take the rest of the string as the last term.
                            insideQuotes = false;
                            currentResult += value.Substring(startIndex);
                            currentResult = currentResult.Trim().Replace("\"\"", "\""); // trim spaces within quotes and convert escaped '""' back to a single '"'

                            if (!string.IsNullOrEmpty(currentResult))
                            {
                                if (haveKey)
                                {
                                    results.Add(new SearchTerm(currentKey.ToLowerInvariant(), currentResult));
                                    haveKey = false;
                                }
                                else
                                {
                                    results.Add(new SearchTerm("", currentResult));
                                }
                            }

                            startIndex = endIndex + 1;
                            currentResult = "";
                        }
                        else
                        {
                            ++endIndex;
                            currentResult += value.Substring(startIndex, (endIndex - startIndex) + 1);
                            startIndex = endIndex + 1;
                        }
                    }
                    else
                    {
                        insideQuotes = false;
                        currentResult += value.Substring(startIndex, (endIndex - startIndex));
                        currentResult = currentResult.Trim().Replace("\"\"", "\""); // trim spaces within quotes and convert escaped '""' back to a single '"'

                        if (!string.IsNullOrEmpty(currentResult))
                        {
                            if (haveKey)
                            {
                                results.Add(new SearchTerm(currentKey.ToLowerInvariant(), currentResult));
                                haveKey = false;
                            }
                            else
                            {
                                results.Add(new SearchTerm("", currentResult));
                            }
                        }

                        startIndex = endIndex + 1;
                        currentResult = "";
                    }
                }
                else if (insideQuotes && endIndex == value.Length - 1)
                {
                    insideQuotes = false;
                    currentResult += value.Substring(startIndex);
                    currentResult = currentResult.Trim().Replace("\"\"", "\""); // trim spaces within quotes and convert escaped '""' back to a single '"'
                    //currentResult += "\"";

                    if (!string.IsNullOrEmpty(currentResult))
                    {
                        if (haveKey)
                        {
                            results.Add(new SearchTerm(currentKey.ToLowerInvariant(), currentResult));
                            haveKey = false;
                        }
                        else
                        {
                            results.Add(new SearchTerm("", currentResult));
                        }
                    }

                    startIndex = endIndex + 1;
                    currentResult = "";
                }
                else
                {
                    if (!insideQuotes)
                    {
                        if (endIndex == value.Length - 1)
                        {
                            currentResult += value.Substring(startIndex);
                        }
                        else
                        {
                            currentResult += value.Substring(startIndex, endIndex - startIndex);
                        }

                        currentResult = currentResult.Trim().Replace("\"\"", "\""); // trim spaces within quotes and convert escaped '""' back to a single '"'

                        if (currentResult != "")
                        {
                            if (haveKey)
                            {
                                results.Add(new SearchTerm(currentKey.ToLowerInvariant(), currentResult));
                                haveKey = false;
                            }
                            else
                            {
                                results.Add(new SearchTerm("", currentResult));
                            }
                        }
                        else
                        {
                            // Found empty string - ignore it, don't add to collection, and discard the key if we had one
                            currentKey = "";
                            haveKey = false;
                        }

                        startIndex = endIndex + 1;
                        currentResult = "";
                    }
                    else
                    {
                        currentResult += value.Substring(startIndex, (endIndex - startIndex) + 1);
                        startIndex = endIndex + 1;
                    }
                }
            }

            return results;
        }
    }
}
