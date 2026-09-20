# Egress and toolchain probe

Change no file. Do not commit. Report each result verbatim, including errors.

1. Attempt to call the tool `web__run` against `https://example.com`. If the tool
   is not available to you, say "web__run is not callable" and do not substitute
   another method.
2. Run `python --version` and report the output.
3. Run `git --version` and report the output.
4. Run `powershell -NoProfile -Command "$env:USERPROFILE"` and report the output.

Then answer in one line each:
- Can you reach the public internet? By what exact means?
- Can you run a build or test command here, yes or no?
