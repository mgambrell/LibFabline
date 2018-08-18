THIS IS A WORK IN PROGRESS

Goal: create a specification for commandline tool arguments which solves deficiencies I've identified in all existing approaches; this will then be implemented in standard libraries for use in all my tools.

I don't like data-driven stuff. It's always too hard to learn and implement. The easiest in both respects is to treat the command line as a short script and process tokens in order. Note that some very complex programs like ffmpeg have taken this approach. 

I also find that it's handy in many cases to set up a project where tools are run on config files. Why not make the command line syntax and config file syntax equivalent? 

Now, I know what you're thinking. I want hierarchical data in my config files; I want to use YAML or XML or even a script file or some ad-hoc language. I've done all those things. But then I realized--this was not a proper separation of concerns. If you want a complex config file, write a script to generate it. In other words, `python myscript_makes_a_cfg.py | mytool.exe -` 

Therefore, we also will take pains to ingest config files on stdin; to that end this library manages the program's execution environment completely for you: it processes the commandline into commands, reads config files as needed, and incorporates stdin as an additional command stream.

Here's the specifications (WIP)

`mytool.exe - mycommand arg - mycommand arg arg` 

is the normal use. if that looks familiar, it should. for familiarity, this style is accepted as well:

`mytool.exe -mycommand arg -mycommand arg arg`

In other words, - is used as a command delimiter.

`mytool.exe - mycommand arg - mycommand arg arg -` 

will read remaining commands from stdin. You've basically said: "here's a command, and....?"

`mytool.exe -`

is needed when you just want to read commands from stdin. That is because

`mytool.exe`

will be commonly used by people wondering what to do, and we don't want it to get stuck reading stdin

`mytool.exe file`

having received something besides a command, knows to interpret that as a file name full of commands; thus we have convenient syntax for this very common case.

`mytool.exe - commands file`

will read the commands from the given command file. We need this, because we might want:

`mytool.exe - mycommand arg - commands file`

which would be a common pattern of setting up some parameters before running a commands file

The special command `commands` is unlikely to be used by any tool; it already IS a list of commands. You wouldnt have a command named `commands`, it's redundant. An alternative would be to use a new symbol, likely @, but I favor `commands`.

Now, in principle, command files can then use `-` as a line-comment. I know that's confusing, but `-` is already reserved for special meaning anyway, and that meaning isn't needed inside the command file. Luckily, if `-` is a comment then so too is `--`, which is more familiar. So, let's use -- as the line-comment. I believe it can be used to end lines without any problem, besides typical parsing context difficulties (i.e. `mycommand "quoted--argument--with--these" --now a real comment` but that's solvable.) Furthermore lines are pre-trimmed, post-trimmed, and empty lines are ignored. 

It isn't fair that you can use ${Variables} or %variables% on the commandline, but not in a commands file. Therefore, I should support one of those--probably the bash style one. OK, you're asking, why not let it be as programmable as bash? Because no.

Now the only remaining problem is a tool which is designed to receive input on stdin:
`mydatasource | mystdinreader.exe - mycommand arg - commands file` -- no problem
`mystdinreader.exe - mycommand arg - commands file < mydatasource` -- no problem

But what if we want to issue commands and input both via stdin? Since the command name `commands` is already reserved, let's end the stream by writing `commands end`. This should be followed by a newline (I do have a rationale for that). As a result of this, the tool can stop processing commands after `commands end` and begin processing the remainder as stdin data.

Newlines are defined to be determined by the 1st of CR, LF, or CRLF encountered; LFCR is assumed not to exist, but as long as it's not preceded by CR (that is, CRLFCR ...lf) then it could be caught as an erroneously garbled file.

If the stream is only one line, then the newline style can't be known; in that case, it's `command end` and it is interpreted as ending the stream immediately after the `d`. In other words, `command end` before any other lines ends at the `d`, and `command end` after any other lines ends after that line.

Now after having read all of the above, why not have `command end` always terminate after the `d`? Because who's going to remember this, that's why. I don't want to have my name cursed every time (admittedly uncommonly) someone joins commands to stdin data and put an extra newline at the end of the commands.

Note: `command end` in a commands file may be useful; you might send it through stdin. For orthogonality, it works the same way as a stdin stream. We will opt to enforce the same rules as above for streams. However, since command files may be used in a more elementary "response file" facility, the `command end` is not required (it only would be if it was stdin'd and concatenated with more stdin data)

`mytool.exe help`
would introduce something I'm wary of, a kind of ur-command. It isn't necessary, either, since `mytool.exe` can detect an empty stdin and print help information. Moreover, it's impossible to reliably distinguish between this and a specified command file. We'd have to have some kind of `mytool.exe @ file_with_same_name_as_ur_command` disambiguator. So, I'm writing this here in case we need it later.

Yes, due to the way this is set up, command arguments beginning with dashes (i.e. negative numbers) need to be quoted. You can't win them all. This would be possible if we defined commands ahead of time or returned them to the tool incrementally for processing, but I'm trying to keep this simple... right?

I might allow for some more custom formatting here to improve that. For instance, we would have `mytool.exe -xofs=-10` be equivalent to `mytool.exe -xofs "-10"`. In thinking about this, we should use to our advantage the C-identifier (see below) rule; it's possible that there are a lot of symbols at our disposal. But I want to wait and see how big a deal this is.

Since the intention is for a library to handle these specifications, there are some additional specifications I do not need to, but do insist on providing:
1. Commands will always be returned as lower-cased strings, regardless of how they're entered by the user. The lower-casing 
2. Commands are valid C identifiers, with the exception that the length is unlimited.

Other ideas for the future:
1. Commands may be permitted to be a valid C expression consisting of dots and subscripts containing integer, string, or float literals or identifiers (in other words, a rudimentary js-like object path expression). This is intended mostly for simple array syntaxes like `mytool.exe - sources[0] file.png - sources[1] overlay.png` but I don't see why it couldn't be expanded. The library might or might not assist with parsing those.
2. An extra library layer for data-driven specifications, the way everyone else likes to do it

Definite future work:
1. C and C++ versions of this library