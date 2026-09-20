# Egress and home probe

Change no file. Do not commit. Report facts, not intentions.

Do exactly these four things and report each result verbatim, including any error
text:

1. Print your HOME and USERPROFILE environment variables, and CODEX_HOME.
   On Windows: `cmd /c echo %USERPROFILE% ^| %HOME% ^| %CODEX_HOME%`
2. List the files directly inside USERPROFILE. State whether `.gitconfig` is
   there, and if so print its contents.
3. Attempt one outbound HTTPS request to `https://example.com` using whatever
   means you have: a dedicated web tool if you hold one, otherwise
   `curl -sS -m 10 -o NUL -w "%{http_code}" https://example.com`. Report the
   status code or the exact failure.
4. State whether a tool named `web__run` is actually callable by you. If it is,
   call it against `https://example.com` and report the outcome. If it is not,
   say "web__run is not callable".

Then answer one question in one line: can you reach the public internet from
here, yes or no, and by what means.
