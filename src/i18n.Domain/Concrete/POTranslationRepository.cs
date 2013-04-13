﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using i18n.Domain.Abstract;
using i18n.Domain.Entities;

namespace i18n.Domain.Concrete
{
	public class POTranslationRepository : ITranslationRepository
	{
		private i18nSettings _settings;

		public POTranslationRepository(i18nSettings settings)
		{
			_settings = settings;
		}

		#region load

/*MC001
		/// <summary>
		/// Retrieves a language queryable for use with LINQ.
		/// This does not take care of thread safety.
		/// </summary>
		/// <param name="tag">The languagetag to get the language for</param>
		/// <returns>Queryable translate items for use with LINQ</returns>
		public IQueryable<TranslateItem> GetLanguageItems(string tag)
		{
			Translation translation = ParseTranslationFile(tag);
			return translation.Items.AsQueryable();
		}
*/

		public Translation GetLanguage(string tag)
		{
			return ParseTranslationFile(tag);
		}

/*MC001
		public ConcurrentDictionary<string, TranslateItem> GetLanguageDictionary(string tag)
		{
			ConcurrentDictionary<string, TranslateItem> messages = new ConcurrentDictionary<string, TranslateItem>();

			//todo: instead of always calling ParseTranslationFile here we could cache the translation and just read from there. If we want to do that the ITranslationRepository needs to add a ReleaseCache or similar for when the file has been changed.
			Translation translation = ParseTranslationFile(tag);


			// Only if a msgstr (translation) is provided for this entry do we add an entry to the cache.
			// This conditions facilitates more useful operation of the IsLanguageValid method,
			// which prior to this condition was indicating a language was available when in fact there
			// were zero translation in the PO file (it having been autogenerated during gettext merge).
			foreach (TranslateItem item in translation.Items)
			{
				if (!string.IsNullOrWhiteSpace(item.Message))
				{
					if (!messages.ContainsKey(item.Id))
					{
						messages[item.Id] = item;
					}
				}
			}

			return messages;
		}
*/

		public IEnumerable<Language> GetAvailableLanguages()
		{
			//todo: ideally we want to fill the other data in the Language object so this is usable by project incorporating i18n that they can simply lookup available languages. Maybe we even add a country property so that it's easier for projects to add corresponding flags.

			DirectoryInfo di = new DirectoryInfo(GetAbsoluteLocaleDir());
			List<Language> dirList = new List<Language>();
			Language language;

			foreach (var dir in di.EnumerateDirectories().Select(x => x.Name))
			{
				language = new Language();
				language.LanguageShortTag = dir;
				dirList.Add(language);
			}
			return dirList;
		}

		public bool TranslationExists(string tag)
		{
			return File.Exists(GetPathForLanguage(tag));
		}

		#endregion

		#region save

		public void SaveTranslation(Translation translation)
		{
			string filePath = GetPathForLanguage(translation.LanguageInformation.LanguageShortTag);
			string backupPath = GetPathForLanguage(translation.LanguageInformation.LanguageShortTag) + ".backup";

			if (File.Exists(filePath)) //we backup one version. more advanced backup solutions could be added here.
			{
				if (File.Exists(backupPath))
				{
					File.Delete(backupPath);
				}
				System.IO.File.Move(filePath, backupPath);
			}

			if (File.Exists(filePath)) //we make sure the old file is removed first
			{
				File.Delete(filePath);
			}

			bool hasReferences = false;

			using (StreamWriter stream = new StreamWriter(filePath))
			{
               // Establish ordering of items in PO file.
                var orderedItems = translation.Items.Values
                    .OrderBy(x => x.References == null || x.References.Count() == 0)
                        // Non-orphan items before orphan items.
                    .ThenBy(x => x.Id);
                        // Then order alphanumerically.
               //
				foreach (var item in orderedItems)
				{
					hasReferences = false;

					if (item.TranslatorComments != null)
					{
						foreach (var translatorComment in item.TranslatorComments)
						{
							stream.WriteLine("# " + translatorComment);
						}
					}

					if (item.ExtractedComments != null)
					{
						foreach (var extractedComment in item.ExtractedComments)
						{
							stream.WriteLine("#. " + extractedComment);
						}
					}

					if (item.References != null)
					{
						foreach (var reference in item.References)
						{
							hasReferences = true;
							stream.WriteLine("#: " + reference);
						}
					}

					if (item.Flags != null)
					{
						foreach (var flag in item.Flags)
						{
							stream.WriteLine("#, " + flag);
						}
					}

					if (hasReferences)
					{
						stream.WriteLine("msgid \"" + escape(item.Id) + "\"");
						stream.WriteLine("msgstr \"" + escape(item.Message) + "\"");
						stream.WriteLine("");
					}
					else
					{
						stream.WriteLine("#~ msgid \"" + escape(item.Id) + "\"");
						stream.WriteLine("#~ msgstr \"" + escape(item.Message) + "\"");
						stream.WriteLine("");
					}
					
				}
			}
		}

		public void SaveTemplate(IDictionary<string, TemplateItem> items)
		{
			string filePath = GetAbsoluteLocaleDir() + "/messages.pot";
			string backupPath = filePath + ".backup";

			if (File.Exists(filePath)) //we backup one version. more advanced backup solutions could be added here.
			{
				if (File.Exists(backupPath))
				{
					File.Delete(backupPath);
				}
				System.IO.File.Move(filePath, backupPath);
			}

			if (File.Exists(filePath)) //we make sure the old file is removed first
			{
				File.Delete(filePath);
			}

			using (StreamWriter stream = new StreamWriter(filePath))
			{
				foreach (var item in items.Values)
				{
					if (item.Comments != null)
					{
						foreach (var comment in item.Comments)
						{
							stream.WriteLine("#. " + comment);
						}
					}

					foreach (var reference in item.References)
					{
						stream.WriteLine("#: " + reference);
					}

					stream.WriteLine("msgid \"" + escape(item.Id) + "\"");
					stream.WriteLine("");
				}
			}
		}

		#endregion

		#region helpers

		private string GetAbsoluteLocaleDir()
		{
			string localeDir = _settings.LocaleDirectory;
			String path;

			if (Path.IsPathRooted(localeDir))
			{
				path = localeDir;
			}
			else
			{
				path = Path.GetFullPath(localeDir);
			}

			return path;
		}

		private string GetPathForLanguage(string tag)
		{
			string path = GetAbsoluteLocaleDir();
			return path + "/" + tag + "/messages.po";
		}

		private Translation ParseTranslationFile(string tag)
		{
			//todo: consider that lines we don't understand like headers from poedit and #| should be preserved and outputted again.

			Translation translation = new Translation();
			Language language = new Language();
			language.LanguageShortTag = tag;
			translation.LanguageInformation = language;
			var items = new ConcurrentDictionary<string, TranslateItem>();

			string path = GetPathForLanguage(tag);

			using (var fs = File.OpenText(path))
			{
				// http://www.gnu.org/s/hello/manual/gettext/PO-Files.html

				string line;
				bool currentlyReadingItem = false;
				while ((line = fs.ReadLine()) != null)
				{
					List<string> extractedComments = new List<string>();
					List<string> translatorComments = new List<string>();
					List<string> flags = new List<string>();
					List<string> references = new List<string>();

					//read all comments, flags and other descriptive items for this string
					//if we have #~ its a historical/log entry but it is the messageID/message so we skip this do/while
					if (line.StartsWith("#") && !line.StartsWith("#~"))
					{
						do
						{
							currentlyReadingItem = true;
							switch (line[1])
							{
								case '.': //Extracted comments
									extractedComments.Add(line.Substring(2).Trim());
									break;
								case ':': //references
									references.Add(line.Substring(2).Trim());
									break;
								case ',': //flags
									flags.Add(line.Substring(2).Trim());
									break;
								case '|': //msgid previous-untranslated-string - NOT used by us
									break;
								default: //translator comments
									translatorComments.Add(line.Substring(1).Trim());
									break;
							}

						} while ((line = fs.ReadLine()) != null && line.StartsWith("#"));
					}

					if (currentlyReadingItem || line.StartsWith("#~"))
					{
						TranslateItem item = ParseBody(fs, line);

                        if (item != null) {
                           //
					        item.TranslatorComments = translatorComments;
					        item.ExtractedComments = extractedComments;
					        item.Flags = flags;
					        item.References = references;
                           //
                            items.AddOrUpdate(
                                item.Id, 
                                // Add routine.
                                k => {
			                        return item;
                                },
                                // Update routine.
                                (k, v) => {
                                    v.References = v.References.Append(item.References);
                                    v.ExtractedComments = v.ExtractedComments.Append(item.References);
                                    v.TranslatorComments = v.TranslatorComments.Append(item.References);
                                    v.Flags = v.Flags.Append(item.References);
                                    return v;
                                });
                        }
					}

					currentlyReadingItem = false;
				}
			}
			translation.Items = items;
			return translation;
		}

		private string RemoveCommentIfHistorical(string line)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				return null;
			}

			if (line.StartsWith("#~"))
			{
				return line.Replace("#~", "").Trim();
			}

			return line;
		}

		private TranslateItem ParseBody(TextReader fs, string line)
		{
			if (string.IsNullOrEmpty(line)) {
                return null; }

            TranslateItem message = new TranslateItem();
			StringBuilder sb = new StringBuilder();

			line = RemoveCommentIfHistorical(line); //so that we read in removed historical records too
			if (line.StartsWith("msgid"))
			{
				var msgid = Unquote(line);
				sb.Append(msgid);

				while ((line = fs.ReadLine()) != null)
				{
					line = RemoveCommentIfHistorical(line);
					if (!line.StartsWith("msgstr") && (msgid = Unquote(line)) != null)
					{
						sb.Append(msgid);
					}
					else
					{
						break;
					}
				}

				message.Id = Unescape(sb.ToString());
			}

			sb.Clear();
			line = RemoveCommentIfHistorical(line);
			if (!string.IsNullOrEmpty(line) && line.StartsWith("msgstr"))
			{
				var msgstr = Unquote(line);
				sb.Append(msgstr);

				while ((line = fs.ReadLine()) != null && (msgstr = Unquote(line)) != null)
				{
					line = RemoveCommentIfHistorical(line);
					sb.Append(msgstr);
				}

				message.Message = Unescape(sb.ToString());
			}
            return message;
		}

		#region quoting and escaping

		//this method removes anything before the first quote and also removes first and last quote
		private string Unquote(string lhs, string quotechar = "\"")
		{
			int begin = lhs.IndexOf(quotechar);
			if (begin == -1)
			{
				return null;
			}
			int end = lhs.LastIndexOf(quotechar);
			if (end <= begin)
			{
				return null;
			}
			return lhs.Substring(begin + 1, end - begin - 1);
		}

		private string escape(string s)
		{
			if (string.IsNullOrWhiteSpace(s))
			{
				return null;
			}
			return s.Replace("\"", "\\\"");
		}

		/// <summary>
		/// Looks up in the subject string standard C escape sequences and converts them
		/// to their actual character counterparts.
		/// </summary>
		/// <seealso href="http://stackoverflow.com/questions/6629020/evaluate-escaped-string/8854626#8854626"/>
		private string Unescape(string s)
		{
			Regex regex_unescape = new Regex("\\\\[abfnrtv?\"'\\\\]|\\\\[0-3]?[0-7]{1,2}|\\\\u[0-9a-fA-F]{4}|.");

			StringBuilder sb = new StringBuilder();
			MatchCollection mc = regex_unescape.Matches(s, 0);

			foreach (Match m in mc)
			{
				if (m.Length == 1)
				{
					sb.Append(m.Value);
				}
				else
				{
					if (m.Value[1] >= '0' && m.Value[1] <= '7')
					{
						int i = 0;

						for (int j = 1; j < m.Length; j++)
						{
							i *= 8;
							i += m.Value[j] - '0';
						}

						sb.Append((char)i);
					}
					else if (m.Value[1] == 'u')
					{
						int i = 0;

						for (int j = 2; j < m.Length; j++)
						{
							i *= 16;

							if (m.Value[j] >= '0' && m.Value[j] <= '9')
							{
								i += m.Value[j] - '0';
							}
							else if (m.Value[j] >= 'A' && m.Value[j] <= 'F')
							{
								i += m.Value[j] - 'A' + 10;
							}
							else if (m.Value[j] >= 'a' && m.Value[j] <= 'a')
							{
								i += m.Value[j] - 'a' + 10;
							}
						}

						sb.Append((char)i);
					}
					else
					{
						switch (m.Value[1])
						{
							case 'a':
								sb.Append('\a');
								break;
							case 'b':
								sb.Append('\b');
								break;
							case 'f':
								sb.Append('\f');
								break;
							case 'n':
								sb.Append('\n');
								break;
							case 'r':
								sb.Append('\r');
								break;
							case 't':
								sb.Append('\t');
								break;
							case 'v':
								sb.Append('\v');
								break;
							default:
								sb.Append(m.Value[1]);
								break;
						}
					}
				}
			}

			return sb.ToString();
		}

		#endregion

		#endregion
	}
}
