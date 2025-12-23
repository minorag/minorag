import { Injectable } from '@angular/core';
import { marked } from 'marked';
import { markedHighlight } from 'marked-highlight';
import hljs from 'highlight.js';
import DOMPurify from 'dompurify';

@Injectable({ providedIn: 'root' })
export class MarkdownService {
  constructor() {
    marked.use(
      markedHighlight({
        langPrefix: 'hljs language-',
        highlight(code: string, lang: string) {
          if (lang && hljs.getLanguage(lang)) {
            return hljs.highlight(code, { language: lang }).value;
          }
          return hljs.highlightAuto(code).value;
        },
      })
    );

    marked.setOptions({ gfm: true, breaks: true });
  }

  toSafeHtml(markdown: string): string {
    const raw = marked.parse(markdown ?? '') as string;

    return DOMPurify.sanitize(raw, {
      USE_PROFILES: { html: true },
      ADD_TAGS: ['pre', 'code', 'table', 'thead', 'tbody', 'tr', 'th', 'td', 'span'],
      ADD_ATTR: ['class'], // <-- critical for hljs classes
    });
  }
}
