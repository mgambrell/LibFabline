using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;
using System.Text;
using System.Collections.Generic;

namespace LibFabline
{
	class FablineEnvironment
	{
		/// <summary>
		/// Prepares the Fabline environment from the given commandline
		/// Will terminate the process with standard error messages if the user's input is wrong
		/// </summary>
		public FablineEnvironment(string[] args, System.IO.Stream stdin)
		{
			if (args == null) throw new ArgumentNullException("args", "args must be provided, even if it's an empty array");
			if (stdin == null) throw new ArgumentNullException("stdin", "stdin must be provided");

			ParseArguments(args);
		}

		void ParseArguments(string[] args)
		{
			bool haveDash = false;
			foreach (var _a in args)
			{
				var a = _a;
				bool isDash = a == "-";
				bool isTreatedAsDash = isDash;
				bool startsDash = a.StartsWith("-");

				//if we got something that isn't a dash, but starts with a dash, treat it as if it was separate
				if (startsDash && !isDash)
				{
					a = a.Substring(1);
					isTreatedAsDash = true;
				}

				//any time we get a dash, we just register it and then move along (unless we already had a dash)
				if (isDash || isTreatedAsDash)
				{
					if (haveDash) InvalidCLISyntax("succession of dashes");
					haveDash = true;
					if(isDash)
						continue;
				}

				string lower = a.ToLowerInvariant();

				if (haveDash)
				{
					if (IsValidCommandToken(lower))
					{
						haveDash = false;
						EmitCommand(lower);
						continue;
					}
					else Bail("invalid command name");
				}

				//we don't have a dash; so, token is assumed to be an argument
				if (currCommand == null)
				{
					//well, if we're not in a command, this is interpreted as a filename (there can be multiple)
					ParseFilename(a);
				}
				else
				{
					EmitArgument(a);
				}
			}

			//if we ended with a dash, then we're supposed to read from stdin
			commandsFromStdin = haveDash;
		}

		class FileParser
		{
			enum CRLFStyle
			{
				Unset,
				CR,LF,CRLF
			}

			public string FullName;
			StreamReader Reader;
			CRLFStyle CrlfStyle;

			string ReadLine()
			{
				StringBuilder line = new StringBuilder();
				if (Reader.Peek() == -1)
					return null;
				for (;;)
				{
					int c = Reader.Read();
					if(c==-1) break;
					if(CrlfStyle == CRLFStyle.CR)
					{
						if (c == '\r')
							break;
					}
					if(CrlfStyle == CRLFStyle.LF)
					{
						if (c == '\n')
							break;
					}
					if (CrlfStyle == CRLFStyle.CRLF)
					{
						if (c == '\r' && Reader.Peek() == '\n')
						{
							Reader.Read();
							break;
						}
					}
					if (CrlfStyle == CRLFStyle.Unset)
					{
						if (c == '\r')
						{
							if (Reader.Peek() == '\n')
							{
								CrlfStyle = CRLFStyle.CRLF;
								Reader.Read();
								break;
							}
							if (Reader.Peek() == '\r')
							{
								CrlfStyle = CRLFStyle.CR;
								break;
							}
						}
						else if (c == '\n')
						{
							CrlfStyle = CRLFStyle.LF;
							break;
						}
					}
					line.Append((char)c);
				}
				
				return line.ToString();
			}

			public FileParser(string filename)
			{
				FullName = Path.GetFullPath(filename);

				try
				{
					//read each line from the input file
					//to stay sane ive split the line reading from the line parsing
					using (Reader = new StreamReader(FullName))
					{
						for (;;)
						{
							var line = ReadLine();
							if (line == null) break;
							line = line.Trim();
							if (line == "") continue;

							//tokenize the line; this is not simple, and surely not complete
							//we have a choice of parsing similar to the command line, or similar to a programming language
							//umm let's be inspired by whatever bash would do
							List<StringBuilder> tokens = new List<StringBuilder>();
							StringBuilder sbToken = null;
							bool hasDash = false;
							bool hasBackslash = false;
							bool inQuoted = false;
							foreach (var c in line)
							{
								bool isQuot = c == '\"';
								bool isDash = c == '-';
								bool isBackslash = c == '\\';
								bool isWhitespace = (c == ' ' || c == '\t');
								if (hasBackslash)
								{
									if (sbToken == null) sbToken = new StringBuilder();
									sbToken.Append(c);
									hasBackslash = false;
									continue;
								}
								if (inQuoted)
								{
									if (isBackslash)
									{
										hasBackslash = true;
										continue;
									}
									if (isQuot)
									{
										inQuoted = false;
										continue;
									}
								}
								
								if (isQuot)
								{
									inQuoted = true;
									continue;
								}
								else if (isDash)
								{
									if (hasDash)
										break;
									hasDash = true;
									continue;
								}

								if (isWhitespace && !inQuoted)
								{
									if (sbToken != null)
									{
										sbToken = null;
									}
								}
								else
								{
									if (sbToken == null)
									{
										sbToken = new StringBuilder();
										tokens.Add(sbToken);
									}
									if(hasDash) sbToken.Append('-');
									hasDash = false;
									sbToken.Append(c);
								}
								hasDash = false;

							}

							if (hasBackslash) InvalidFileSyntax(filename, "Line terminates with incomplete escape character");
							if (inQuoted) InvalidFileSyntax(filename, "Line terminates with incomplete quoted string");

							var doneTokens = tokens.Select(t => t.ToString()).ToList();

							if (doneTokens.Count == 0) continue;
							if (!IsValidCommandToken(doneTokens[0])) InvalidFileSyntax(filename, "invalid command name");
							doneTokens[0] = doneTokens[0].ToLowerInvariant();
							EmitCommand(doneTokens[0]);
							for (int i = 1; i < tokens.Count; i++)
								EmitArgument(doneTokens[i]);

						} //LINE LOOP
					} //FILE READER
				} //TRY
				catch
				{
					Bail("Error reading specified file: " + filename);
				}

				Reader = null;
			}

			FablineCommand currCommand;
			public List<FablineCommand> Commands = new List<FablineCommand>();
			void EmitCommand(string command)
			{
				currCommand = new FablineCommand() { Name = command };
				Commands.Add(currCommand);
			}

			void EmitArgument(string argument)
			{
				currCommand.Args.Add(argument);
			}

		} //class FileParser

		void ParseFilename(string filename)
		{
			var parser = new FileParser(filename);
			FablineCommand fileCommand = new FablineCommand();
			fileCommand.Name = "<file>";
			fileCommand.Args.Add(parser.FullName);
			Commands.Add(fileCommand);
			Commands.AddRange(parser.Commands);
		}

		/// <summary>
		/// Empty is set if the commandline was totally empty.
		/// </summary>
		public bool Empty { get; private set; }

		FablineCommand currCommand;
		public List<FablineCommand> Commands = new List<FablineCommand>();
		void EmitCommand(string command)
		{
			currCommand = new FablineCommand() { Name = command };
			Commands.Add(currCommand);
		}

		void EmitArgument(string argument)
		{
			currCommand.Args.Add(argument);
		}

		bool commandsFromStdin;

		static System.Text.RegularExpressions.Regex rxIdentifier = new System.Text.RegularExpressions.Regex("[_a-zA-Z][_a-zA-Z0-9]", System.Text.RegularExpressions.RegexOptions.Compiled);

		static bool IsValidCommandToken(string value)
		{
			return rxIdentifier.IsMatch(value);
		}

		static void InvalidCLISyntax(string message)
		{
			Bail("Invalid CLI syntax: " + message);
		}

		static void InvalidFileSyntax(string filename, string message)
		{
			Bail("Invalid syntax in file " + filename + ": " + message);
		}

		static void Bail(string message)
		{
			Console.Error.WriteLine(message);
			Environment.Exit(1);
		}
	}

	/// <summary>
	/// One of the commands provided by the user
	/// </summary>
	public class FablineCommand
	{
		/// <summary>
		/// The name of the command
		/// </summary>
		public string Name;

		/// <summary>
		/// The arguments that were provided to the command
		/// </summary>
		public List<string> Args = new List<string>();
	}
}
