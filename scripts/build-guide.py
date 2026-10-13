#!/usr/bin/env python3
"""Build the players' firing guide as one self-contained HTML page.

Reads the guide's Markdown source (firing-guide.md in the docs folder, or
FIRING-GUIDE.md at the top of the public source) and writes a page
that opens from the mod folder with a double-click: styled, with a contents
list, light or dark to suit the reader, and nothing to load from elsewhere.
Requires the Python Markdown package (python3 -m pip install markdown).

Usage: build-guide.py OUTPUT.html
"""

import html
import json
import pathlib
import sys

try:
    import markdown
except ImportError:
    sys.exit("build-guide.py needs the Python Markdown package: python3 -m pip install markdown")

REPO = pathlib.Path(__file__).resolve().parent.parent
SOURCE = next((p for p in (REPO / "docs" / "firing-guide.md", REPO / "FIRING-GUIDE.md") if p.is_file()),
              REPO / "FIRING-GUIDE.md")
INFO = REPO / "src" / "RealisticFiring" / "Info.json"

STYLE = """
:root {
  --text: #1f1d1a; --muted: #5e5850; --back: #fbf9f6; --panel: #f1ede6;
  --rule: #ddd5c8; --accent: #b4441c; --accent-soft: #f6e3d8;
}
@media (prefers-color-scheme: dark) {
  :root {
    --text: #ece7df; --muted: #a9a196; --back: #1b1917; --panel: #26231f;
    --rule: #3b3631; --accent: #f08a4b; --accent-soft: #3a2a20;
  }
}
* { box-sizing: border-box; }
body {
  margin: 0; background: var(--back); color: var(--text);
  font: 17px/1.6 system-ui, -apple-system, "Segoe UI", Roboto, "Noto Sans", sans-serif;
}
header { background: var(--panel); border-bottom: 3px solid var(--accent); }
header div, main { max-width: 46rem; margin: 0 auto; padding: 0 1.25rem; }
header div { padding-top: 1.6rem; padding-bottom: 1.2rem; }
header h1 { margin: 0; font-size: 2rem; line-height: 1.2; }
header p { margin: .3rem 0 0; color: var(--muted); }
main { padding-bottom: 3rem; }
nav { margin: 1.5rem 0 .5rem; padding: .8rem 1.1rem; background: var(--panel); border-radius: 8px; }
nav strong { display: block; margin-bottom: .3rem; color: var(--muted); font-size: .85rem;
  text-transform: uppercase; letter-spacing: .06em; }
nav ol { margin: 0; padding-left: 1.3rem; columns: 2; column-gap: 2rem; }
nav li { break-inside: avoid; }
h2 { margin: 2.4rem 0 .6rem; padding-top: .9rem; border-top: 1px solid var(--rule);
  font-size: 1.45rem; color: var(--accent); }
h3 { margin: 1.6rem 0 .4rem; font-size: 1.15rem; }
p { margin: .6rem 0; }
ul, main > ol { margin: .5rem 0; padding-left: 1.3rem; }
main > ol > li::marker { font-weight: 700; }
li { margin: .35rem 0; }
li::marker { color: var(--accent); }
strong { color: var(--text); }
a { color: var(--accent); }
code { font-family: ui-monospace, "SFMono-Regular", Consolas, monospace; font-size: .9em;
  background: var(--panel); padding: .05em .3em; border-radius: 4px; }
table { width: 100%; border-collapse: collapse; margin: .8rem 0; font-size: .95rem; }
th, td { text-align: left; padding: .45rem .7rem; border-bottom: 1px solid var(--rule); vertical-align: top; }
th { background: var(--accent-soft); }
tr:nth-child(even) td { background: var(--panel); }
footer { color: var(--muted); font-size: .85rem; margin-top: 2.5rem; }
@media (max-width: 40rem) { nav ol { columns: 1; } body { font-size: 16px; } }
@media print {
  body { background: #fff; color: #000; font-size: 11pt; }
  header { background: none; border-bottom: 2px solid #000; }
  nav { display: none; }
  h2 { color: #000; break-after: avoid; }
  tr:nth-child(even) td, th { background: none; }
}
"""


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__.strip().splitlines()[-1])
    output = pathlib.Path(sys.argv[1])
    version = json.loads(INFO.read_text(encoding="utf-8-sig"))["Version"]
    text = SOURCE.read_text(encoding="utf-8")

    # The page's own header carries the title; the body starts at the first section.
    lines = text.splitlines()
    title = lines[0].lstrip("# ").strip() if lines and lines[0].startswith("# ") else "Firing guide"
    body_md = "\n".join(lines[1:])

    md = markdown.Markdown(extensions=["tables", "sane_lists", "toc"],
                           extension_configs={"toc": {"permalink": False}})
    body = md.convert(body_md)
    sections = [t for t in md.toc_tokens if t["level"] == 2] or \
        [c for t in md.toc_tokens for c in t.get("children", []) if c["level"] == 2]
    contents = "\n".join(f'<li><a href="#{t["id"]}">{html.escape(t["name"], quote=False)}</a></li>' for t in sections)

    page = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Realistic Firing: {html.escape(title)}</title>
<style>{STYLE}</style>
</head>
<body>
<header><div>
<h1>{html.escape(title)}</h1>
<p>Realistic Firing {html.escape(version)} for Derail Valley</p>
</div></header>
<main>
<nav><strong>Contents</strong><ol>
{contents}
</ol></nav>
{body}
<footer>Realistic Firing {html.escape(version)}. The README in the mod folder covers every control.</footer>
</main>
</body>
</html>
"""
    output.write_text(page, encoding="utf-8")
    print(f"wrote {output}")


if __name__ == "__main__":
    main()
