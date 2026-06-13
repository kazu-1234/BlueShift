#!/bin/sh
# filter-branch --msg-filter 用: Cursor attribution 行を除去
while IFS= read -r line || [ -n "$line" ]; do
  case "$line" in
    Co-authored-by:*[Cc]ursor*) ;;
    Co-authored-by:*cursoragent@cursor.com*) ;;
    Made-with:*[Cc]ursor*) ;;
    Made-with:*Cursor*) ;;
    *cursoragent@cursor.com*) ;;
    *) printf '%s\n' "$line" ;;
  esac
done
