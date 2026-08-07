#!/usr/bin/env python3
"""Generate fixture PDFs for the local Grimoire stack.

Uses PyMuPDF — the same library Grimoire reads PDFs with — so anything written
here is parseable by the indexer. Install with: sudo apt-get install -y python3-fitz
(the devcontainer image does this; rebuild the container if the import fails).

Usage: make-fixtures.py <path> <pages>
"""
import sys

try:
    import fitz
except ImportError:
    sys.exit(
        "python3-fitz (PyMuPDF) is required to generate fixtures.\n"
        "  devcontainer: rebuild the container, or "
        "sudo apt-get install -y python3-fitz"
    )


def make_pdf(path: str, pages: int) -> None:
    doc = fitz.open()
    for i in range(pages):
        page = doc.new_page()
        page.insert_text((72, 72), f"grimoire-cli fixture — page {i + 1}")
    doc.save(path)
    doc.close()


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit("Usage: make-fixtures.py <path> <pages>")
    make_pdf(sys.argv[1], int(sys.argv[2]))
