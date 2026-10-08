"""Checks that every relative link and #anchor in docs/ (and the README) points at something that exists.

    python tools/check-docs.py

Exit code 1 when a link is broken. Run it after changing any page in docs/.
"""
import os
import re
import sys

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
pages = [os.path.join(root, "README.md")] + [
    os.path.join(d, f) for d, _, files in os.walk(os.path.join(root, "docs")) for f in files if f.endswith(".md")
]


def anchors(path):
    """GitHub-style heading anchors: lower case, punctuation removed, spaces to hyphens."""
    found = set()
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            m = re.match(r"^#{1,6}\s+(.*)", line)
            if m:
                text = re.sub(r"[^\w\- ]", "", m.group(1).strip().lower())
                found.add(text.replace(" ", "-"))
    return found


broken, checked = [], 0
for page in pages:
    with open(page, encoding="utf-8") as fh:
        text = fh.read()
    for target in re.findall(r"\]\(([^)\s]+)\)", text):
        if target.startswith(("http://", "https://", "mailto:")):
            continue
        checked += 1
        file, _, anchor = target.partition("#")
        dest = os.path.normpath(os.path.join(os.path.dirname(page), file)) if file else page
        if not os.path.exists(dest):
            broken.append(f"{os.path.relpath(page, root)}: {target} (no such file)")
        elif anchor and dest.endswith(".md") and anchor not in anchors(dest):
            broken.append(f"{os.path.relpath(page, root)}: {target} (no such heading)")

print(f"{checked} links checked in {len(pages)} pages")
for b in broken:
    print("  BROKEN", b)
sys.exit(1 if broken else 0)
