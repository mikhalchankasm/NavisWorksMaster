# Write and egress probe

Report each result verbatim, including any error.

1. Create the file `.codex-runs/write_probe.txt` inside your working root with the
   single line `executor can write`. Then read it back and print the contents.
   If the write is refused, print the exact refusal.
2. Attempt to call the tool `web__run` against `https://example.com`. If it is not
   available, say "web__run is not callable".
3. Run `git status --porcelain` and print the output.

Do not commit. Do not change any other file.

Then answer in one line each:
- Can you write files in the working root, yes or no?
- Can you reach the public internet, yes or no, and by what exact means?
