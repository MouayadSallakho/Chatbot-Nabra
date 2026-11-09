# Phase 38.3: Knowledge, agents, channels and the public chat pages

Reported 2026-11-09. Evidence: `C:\Users\mouay\Desktop\nabra-frontend-evidence\p38-3\`. Not committed.

## 1. Summary

- Knowledge now says how indexing went after a reload: "Indexing now", "Indexing failed" with the reason, "Indexing stopped", or never indexed. The list shows the file's real size.
- A refused file says why in English and Arabic, with its limit: "The file has more than 500 pages", a password-protected PDF, content that does not match its type.
- Refusals on the knowledge, agent, website chat, WhatsApp and workspace-setup screens are worded by code in both languages, and the agent screens make two fewer requests each.
- The widget settings offer the Start and End corners, explained in words, and say that visitors see the workspace's name. The chat link has a language choice.
- The hosted chat shows the workspace's name, greeting, accent and theme, with no flash of the wrong theme, and it opens in Arabic or English from the first paint.

## 2. Starting point

Branch `master`, HEAD `91d6f36` (38.2b), clean tree, no `frontend/e2e/.e2e-state/`. The first check of this step found 38.2b uncommitted while your checkpoint was still running, so I stopped and reported it. Your message confirmed the checkpoint, and I checked it again before starting.

**Your addition, done:** `frontend/.gitignore` now has `.e2e-state/`, which matches at any depth, with a corrected comment. Proof on a throwaway file at the real path: `git check-ignore -v e2e/.e2e-state/owner.json` answers `frontend/.gitignore:49:.e2e-state/`, and `git status --ignored` shows `!! e2e/.e2e-state/`.

## 3. Changes by area

`git status --short` (55 modified, 13 new, plus this report; nothing in `backend/` or `deploy/`) is saved in `gates\git-status.txt`.

| Area | What changed and why |
| --- | --- |
| Knowledge | `knowledge-api.ts` reads `lastIndexAttempt` and `sourceByteLength`. `knowledge-words.ts` derives the state to show and words the 8 reasons. The list shows state, reason or "Never indexed", and the file size or "Typed text". The editor shows the state, the reason and the attempt times. |
| File refusals | `knowledge-problems.ts`: all 20 `knowledge.file.*` codes, 6 of them with their bound where it has a clear unit. `uploadFailure` uses the catalogue first and keeps the old wording for a code nobody registered. |
| Refusal catalogues | `agent-problems.ts`, `channel-problems.ts`, `workspace-problems.ts`, `shared/chat/chat-problems.ts`, each loaded with its feature. `hasProblemWording` added to the registry. |
| Agents | `agent-channels.ts` now only maps `channels` and reads the widget's look. Both channel-list reads are gone, and "Not checked" went with them because the case no longer exists. |
| Widget settings | Four corners; the preview draws them for the reader's language (`launcher-position.ts`, shared with the widget frame). A note says visitors see the workspace's name, and the preview shows it. |
| Chat link | "Chat page language": Automatic, English, العربية. It adds `?lang=` to the link that is shown, copied and opened. Nothing is saved. |
| Hosted page | Name, greeting, accent (`pageAccent`, strict `#RRGGBB`), theme. Only the loading lockup shows until the public read answers. |
| Widget | Heads the panel with `workspaceName`. |
| Pre-paint script | `?lang=en\|ar` on `/chat/` and `/embed/`, still one inline script. |
| `.gitignore` | See section 2. |

**Adopted contracts**

| Screen | Endpoint or code | Fields read | Tests |
| --- | --- | --- | --- |
| Knowledge list | `GET …/knowledge` | `lastIndexAttempt`, `sourceByteLength` | `knowledge-list.spec`, e2e `knowledge-index` |
| Knowledge editor | `GET …/knowledge/{id}` | `lastIndexAttempt { status, failureCode, startedAt, completedAt }` | `knowledge-editor.spec`, `arabic-render.spec` |
| Upload and replace | `knowledge.file.*` | `code`, `limit` | `knowledge-problems.spec`, `knowledge-file.spec`, `knowledge-upload.spec`, `arabic-render.spec` |
| Agent list | `GET …/ai-agents` | `channels[].kind/id/status` | `agent-list.spec`, `agent-words.spec` |
| Agent editor | `GET …/ai-agents/{id}`, then the widget read by channel id | `channels` | `agent-editor.spec`, e2e `agent-preview-reply` |
| Widget settings | widget GET and PUT | `launcherPosition` (4 values), `website-chat.*` | `website-chat-widget.spec`, `launcher-position.spec`, e2e `widget-corners` |
| Chat link | client only | `?lang=` | `website-chat-detail.spec` |
| Hosted page | `GET /api/public/webchat/{key}` | `workspaceName`, `greeting`, `accentColor`, `theme` | `hosted-chat.spec`, `accent-color.spec`, e2e `hosted-page-ar` |
| Widget frame | `GET /api/public/embed/{key}` | `workspaceName` | `embed-chat.spec` |
| Pre-paint | none | `location.search` | `theme-startup.spec` (8 new cases) |
| Visitor refusals | `chat.message.*` | `code` | `hosted-chat.spec`, `channel-problems.spec` |
| Workspace setup | `workspace.name.invalid`, `workspace.slug.*` | `code` | `workspace-setup.spec`, `channel-problems.spec` |

**Screenshots.** Before: `omniai-redesign-evidence\p36-08\before\` and `p36-09\before\`. After: `p38-3\chrome\shots\`, for example `knowledge-list-indexing-now.png`, `knowledge-list-failed-ar.png`, `upload-refused-pages-ar.png`, `widget-settings-ar.png`, `widget-End-ar.png`, `hosted-ar.png`, `hosted-accent-ffe066.png`, `hosted-theme-Light-on-dark.png`.

## 4. Decisions

- **No polling for the indexing outcome.** The API publishes no event for it, and the plan names no timer. "Indexing now" stays until the next read, and the editor says to reload.
- **Which state wins.** Disabled first, then a running attempt. Failed and stopped only show while the document still needs an index, because an indexed document is answering.
- **A write never claims "never attempted".** Writes carry no `lastIndexAttempt`, so the editor keeps the last attempt it read and shows no attempt facts when it has none.
- **Plan wording, one small difference.** The plan's "Indexing stopped before it finished" is split into the badge "Indexing stopped" and the line "It stopped before it finished", because the full sentence is too long for a badge.
- **The hosted accent is stricter than the widget's.** As you and the plan specified: exact `#RRGGBB`, white or near-black ink, otherwise Nabra's own look. The widget keeps its 36.9 rule (black as last resort), since this step did not ask to change it.
- **Nabra's mark is hidden on a workspace-coloured header**, as the widget has always done.
- **While the page opens.** The boot lockup shows until the public read answers; after that, the panel's "Opening the chat…" covers the resume read in the right theme.
- **The agent preview keeps the agent's name as its title.** It previews the agent, and it was not in scope.
- **Arabic bounds read "… أكبر من الحد المسموح به، وهو 500"**, so one sentence serves every number without plural forms.
- **`knowledge.file.archive_too_large` names no number**, because its `limit` can be entries or bytes.
- **C10 not adopted**, as the plan records: 37.1 changed the snippet attribute. The plan's "10 WhatsApp codes" is 9 in `problem-codes.md`, plus the entitlement code; `whatsapp.id.invalid` cannot be reached by this client.
- **Two catalogues register in the component constructor** (hosted chat, workspace setup), because their routes sit in the eagerly loaded route table and a route provider would add the words to the startup JavaScript.
- **Deferred:** nothing from the plan's list for this step.

## 5. Verification

| Check | Result |
| --- | --- |
| `npm run format:check` | PASS |
| `npm run test:ci` | PASS: 115 files, 1,612 tests, 0 failures. Free memory before: 7.1 GB |
| `npm run build` | PASS: 0 warnings, budgets kept. Free memory before: 7.1 GB |
| Startup JavaScript | **144,620 gzip**: +427 on 38.2b's 144,193; +4.11% on the 38.0 baseline of 138,907 (ceiling 152,797). Inside the 145,000 target, 380 B left |
| `npm run i18n:check` | PASS: 2,238 messages, 0 errors, 0 warnings |
| `npm run e2e` | PASS: 34 journeys (17 in each language), 34 passed, 0 failed, 1.7 min, no retries. Free memory before: 7.1 GB |
| Backend gates | NOT RUN: no backend change (`git status -- backend deploy` is empty) |
| Chrome matrix (installed Chrome through Playwright with a temporary profile, so the demo password never enters a tool argument) | PASS: 7 routes × 4 widths × 2 themes × 2 languages = 112 rows. Page overflow 0, console errors 0, one `h1` on all, axe WCAG 2.2 AA violations 0. My rough "unnamed control" count flagged only `/knowledge/upload` (16 rows): the deliberately hidden file input (`aria-hidden`, `tabindex="-1"`), reached through its named "Choose a file" button |
| Agent requests, measured on the old code (91d6f36) and the new | PASS: agents list **3 → 1**, agent editor **5 → 3**; both channel-list reads gone |
| Knowledge, stand-in: reload while indexing | PASS: "Indexing now", then "Indexed" after the attempt ended. **Method:** the stand-in indexes in moments, so the test held the page's real, unmocked read until the attempt was running, with the index sent from a second tab (see section 9) |
| Knowledge: real failure | PASS, with the API restarted without the stand-in: `provider_unavailable`, shown as "Indexing failed" and the reason in both languages. Stand-in restored afterwards |
| Knowledge: never attempted, stopped | PASS: "Never" and "Never indexed"; a cancelled request read as "Indexing stopped" |
| Refused files, real server, both languages | PASS: too many pages (500), password-protected, type mismatch, no text, not UTF-8. The English `detail` was never shown |
| Hosted page, private contexts | PASS: `?lang=ar`, `?lang=en`, no parameter with an Arabic and an English browser, `?lang=xx`, and a stored admin choice ignored. Right language and direction before the application loaded |
| Hosted page: name, greeting, accent, theme | PASS: chat never drawn in the other theme (Light on a dark device, Dark on a light device, System both ways) |
| Hosted page: send, reply, failure | PASS in both languages: a stand-in reply, New chat, and the failure notice in the page's language |
| Accent fallback | PASS: unit tests for invalid values and `#2e8b57`, and a sweep of 4,096 colours, all at least 4.5:1. Live: `#2e8b57` keeps Nabra's look |
| Widget corners on an English and an Arabic host page | PASS: all 8 cases, frame and launcher. Host page: `omniai-redesign-evidence\p36-01\widget-host\` on 127.0.0.1:8099. The open panel shows the workspace's name |
| Contrast | PASS: axe over the matrix; ink on accent computed (white on `#1d3557`, `#111827` on `#ffe066`) |
| Reduced motion | PASS: focus transitions run for 0.01 ms when asked; this step adds no animation |
| RTL mirroring, keyboard | PASS: Send on the left in Arabic; hosted page, both new selects and a knowledge row by keyboard with visible focus |
| Mailpit | NOT RUN: this step sends no email |
| Provider canary | PASS at the start, with the stand-in off, after restoring it, before e2e, and at the end. No live traffic |

Rule 12's floor held for every gate. The demo-password scan of 103 evidence files found none, and the temporary cookie jar is deleted.

## 6. See it live

Dev server `http://localhost:4200`, API with the stand-in, both left running. Sign in as the **Owner** from `omniai-redesign-evidence\p36-01\demo-accounts.md`; workspace Northwind Operations.

| Where | What to try |
| --- | --- |
| `/knowledge` | The states, reasons and sizes in the list. "P38.3 …" documents are synthetic |
| `/knowledge/upload` | Files in `p38-3\fixtures\` (for example `too-many-pages.pdf`, `password-protected.pdf`, `not-really.pdf`), in English and Arabic |
| `/ai-agents` | Channels in the list, with no channel-list requests in the Network panel |
| `/channels/website-chat`, then "P38.3 Visitor Desk" | Chat page language; the four corners; the note about the workspace's name; change the accent or theme and open the chat page |
| `http://localhost:4200/chat/vIK-hU7iV-Zeas8c3PMgTA?lang=ar` | The hosted page in Arabic, in a private window. Also `?lang=en`, or no parameter with an Arabic browser |

To see **"Indexing now"** you need a second tab: reloading the tab that pressed Index cancels the request (section 9), and the stand-in finishes in moments.

## 7. Backend asks

| Id | Ask | Shipped instead |
| --- | --- | --- |
| BA-3 (from the plan) | `v1.js` adds the host page's language to the frame URL as `lang=en\|ar` | The widget follows the visitor's browser. Once the frame URL carries `lang`, the pre-paint script applies it with no frontend change |
| New | Let an index attempt finish when its browser request is cancelled | A reload of the tab that pressed Index cancels the request, and the document reads "Indexing stopped" (true, and honest) |
| New | A signal that indexing ended, or a read to wait on | "Indexing now" stays until the page is read again, and the editor says to reload |
| New | Name the unit of `limit` on `knowledge.file.archive_too_large` | The sentence gives no number |

## 8. New Arabic for my review

All 94 new strings, with their English, grouped by where they appear. Two earlier strings were removed with the channel-list reads ("Not checked", "The channels could not be checked just now.").

**Knowledge: states, reasons and the list**

| English | Arabic |
| --- | --- |
| Indexing is running. Reload this page in a moment to see how it ended. | تجري الفهرسة الآن. أعد تحميل هذه الصفحة بعد قليل لترى كيف انتهت. |
| The last attempt stopped before it finished, so this document still needs an index. Try again. | توقفت المحاولة الأخيرة قبل اكتمالها، فما زال هذا المستند يحتاج إلى فهرسة. حاول مرة أخرى. |
| Last attempt started | بدء آخر محاولة |
| Last attempt finished | انتهاء آخر محاولة |
| Never indexed | لم يُفهرس بعد |
| Indexing now | تجري الفهرسة الآن |
| Indexing stopped | توقفت الفهرسة |
| The AI provider could not be reached. | تعذّر الوصول إلى مزوّد الذكاء الاصطناعي. |
| The AI provider took too long to answer. | استغرق مزوّد الذكاء الاصطناعي وقتًا أطول من اللازم للرد. |
| The AI provider's answer could not be used. | تعذّر استخدام رد مزوّد الذكاء الاصطناعي. |
| This workspace has used all the AI usage its plan allows. | استنفدت مساحة العمل هذه استخدام الذكاء الاصطناعي الذي تسمح به باقتها. |
| The document changed while it was being indexed. | تغيّر المستند أثناء فهرسته. |
| Indexing was cancelled. | أُلغيت الفهرسة. |
| Indexing was interrupted before it finished. | انقطعت الفهرسة قبل اكتمالها. |
| Something went wrong while indexing. | حدث خطأ أثناء الفهرسة. |
| It stopped before it finished. | توقفت قبل اكتمالها. |
| Typed text | نص مكتوب |

**Knowledge refusals (upload, editor, agent knowledge)**

| English | Arabic |
| --- | --- |
| This file is past one of the limits a document may have. Split it and upload the part that matters. | تجاوز هذا الملف أحد الحدود المسموح بها للمستند. قسّمه وارفع الجزء المهم منه. |
| This file could not be sent. Reload the page and try again. | تعذّر إرسال هذا الملف. أعد تحميل الصفحة وحاول مرة أخرى. |
| Choose a file before uploading. | اختر ملفًا قبل الرفع. |
| This file's content does not match its type. Save it again in the right format and upload it. | محتوى هذا الملف لا يطابق نوعه. احفظه مرة أخرى بالصيغة الصحيحة ثم ارفعه. |
| This text file is not saved as UTF-8. Save it as UTF-8 and upload it again. | لم يُحفظ هذا الملف النصي بترميز UTF-8. احفظه بهذا الترميز ثم ارفعه مرة أخرى. |
| This file could not be read. It may be damaged. Try saving it again. | تعذّرت قراءة هذا الملف، وقد يكون تالفًا. جرّب حفظه مرة أخرى. |
| This PDF is protected with a password. Remove the password and upload it again. | ملف PDF هذا محميّ بكلمة مرور. أزل كلمة المرور ثم ارفعه مرة أخرى. |
| No text could be found in this file. Scanned pages and images are not read. | لم يُعثر على نص في هذا الملف. الصفحات الممسوحة ضوئيًا والصور لا تُقرأ. |
| This CSV file needs a first row that names its columns. | يجب أن يحوي الصف الأول من ملف CSV هذا أسماء أعمدته. |
| The file has more than {$limit} pages. Split it and upload the part that matters. | عدد صفحات الملف أكبر من الحد المسموح به، وهو {$limit}. قسّمه وارفع الجزء المهم منه. |
| The file holds more than {$limit} characters of text. Split it and upload the part that matters. | عدد أحرف النص في الملف أكبر من الحد المسموح به، وهو {$limit}. قسّمه وارفع الجزء المهم منه. |
| The file has more than {$limit} rows. Split it and upload the part that matters. | عدد صفوف الملف أكبر من الحد المسموح به، وهو {$limit}. قسّمه وارفع الجزء المهم منه. |
| The file has more than {$limit} columns. Remove the ones you do not need and upload it again. | عدد أعمدة الملف أكبر من الحد المسموح به، وهو {$limit}. احذف ما لا تحتاج إليه ثم ارفعه مرة أخرى. |
| A cell in the file is longer than {$limit} characters. Shorten it and upload the file again. | عدد أحرف إحدى خلايا الملف أكبر من الحد المسموح به، وهو {$limit}. اختصرها ثم ارفع الملف مرة أخرى. |
| This Word document is too large once opened. Split it, or save a simpler copy. | حجم مستند Word هذا بعد فتحه أكبر من المسموح به. قسّمه أو احفظ نسخة أبسط منه. |
| This file has no name. Rename it and upload it again. | ليس لهذا الملف اسم. سمِّه ثم ارفعه مرة أخرى. |
| This file's name could not be read. Rename it and upload it again. | تعذّرت قراءة اسم هذا الملف. غيّر اسمه ثم ارفعه مرة أخرى. |
| This file's name is too long. Shorten it and upload it again. | اسم هذا الملف طويل جدًا. اختصره ثم ارفعه مرة أخرى. |
| Give this document a title. | اكتب عنوانًا لهذا المستند. |
| This title is too long. Shorten it and save again. | هذا العنوان طويل جدًا. اختصره ثم احفظ مرة أخرى. |
| A title has to be one line of plain text. | يجب أن يكون العنوان سطرًا واحدًا من نص عادي. |
| Write something in this document. | اكتب شيئًا في هذا المستند. |
| This document is too long. Shorten it and save again. | هذا المستند طويل جدًا. اختصره ثم احفظ مرة أخرى. |
| This text has characters that cannot be saved. Paste it as plain text and try again. | يحوي هذا النص رموزًا لا يمكن حفظها. الصقه نصًا عاديًا وحاول مرة أخرى. |
| This document is already in use. | هذا المستند قيد الاستخدام بالفعل. |
| This document is already out of use. | هذا المستند خارج الاستخدام بالفعل. |
| This document is being indexed right now, so nothing was sent. Reload in a moment to see how it ended. | تجري فهرسة هذا المستند الآن، فلم يُرسَل شيء. أعد التحميل بعد قليل لترى كيف انتهت. |
| This document is too long to index. Split it into smaller documents. | هذا المستند أطول من أن تتم فهرسته. قسّمه إلى مستندات أصغر. |
| This document was written here, so there is no file to replace. | كُتب هذا المستند هنا، فليس له ملف يُستبدل. |
| One of these documents is no longer in this workspace. Reload the page and choose again. | أحد هذه المستندات لم يعد في مساحة العمل هذه. أعد تحميل الصفحة واختر من جديد. |
| An agent cannot draw on this many documents. Remove some and save again. | لا يمكن للوكيل الاعتماد على هذا العدد من المستندات. أزل بعضها ثم احفظ مرة أخرى. |
| Write a question to test. | اكتب سؤالًا لتجربته. |
| This question is too long. Shorten it and try again. | هذا السؤال طويل جدًا. اختصره وحاول مرة أخرى. |
| This workspace has reached the number of knowledge documents its plan allows. Nothing was saved. | بلغت مساحة العمل هذه عدد مستندات المعرفة الذي تسمح به باقتها، ولم يُحفظ شيء. |

**AI agent refusals**

| English | Arabic |
| --- | --- |
| Give this agent a name. | اكتب اسمًا لهذا الوكيل. |
| This name is too long. Shorten it and save again. | هذا الاسم طويل جدًا. اختصره ثم احفظ مرة أخرى. |
| A name has to be plain text on one line. | يجب أن يكون الاسم نصًا عاديًا في سطر واحد. |
| Write instructions for this agent. | اكتب تعليمات لهذا الوكيل. |
| These instructions are too long. Shorten them and save again. | هذه التعليمات طويلة جدًا. اختصرها ثم احفظ مرة أخرى. |
| These instructions have characters that cannot be saved. Paste them as plain text and try again. | تحوي هذه التعليمات رموزًا لا يمكن حفظها. الصقها نصًا عاديًا وحاول مرة أخرى. |
| A draft was never in service, so there is nothing to switch off. | لم تدخل المسودة الخدمة قط، فلا يوجد ما يُعطَّل. |
| This message cannot be sent. Check that it is not blank or too long. | لا يمكن إرسال هذه الرسالة. تأكد من أنها ليست فارغة أو طويلة جدًا. |
| This workspace has reached the number of AI agents its plan allows. Nothing was saved. | بلغت مساحة العمل هذه عدد وكلاء الذكاء الاصطناعي الذي تسمح به باقتها، ولم يُحفظ شيء. |

**Website chat: settings and the chat page language**

| English | Arabic |
| --- | --- |
| Chat page language | لغة صفحة الدردشة |
| Automatic | تلقائية |
| Automatic follows each visitor's browser. Choosing a language adds it to the link. | تتبع التلقائية متصفح كل زائر. واختيار لغة يضيفها إلى الرابط. |
| Visitors see your workspace's name at the top of the chat. | يرى الزوار اسم مساحة العمل في أعلى الدردشة. |
| Start: right in Arabic pages, left in English pages | البداية: اليمين في الصفحات العربية، واليسار في الصفحات الإنجليزية |
| End: left in Arabic pages, right in English pages | النهاية: اليسار في الصفحات العربية، واليمين في الصفحات الإنجليزية |

**Website chat refusals**

| English | Arabic |
| --- | --- |
| Choose an agent for this chat link. | اختر وكيلًا لرابط الدردشة هذا. |
| This agent cannot be used: it already has a chat link, or it is no longer in this workspace. | لا يمكن استخدام هذا الوكيل: فله رابط دردشة بالفعل، أو لم يعد في مساحة العمل هذه. |
| One of the websites is not a valid address. Use the scheme and domain only, with no path. | أحد المواقع ليس عنوانًا صالحًا. اكتب البروتوكول والنطاق فقط، دون مسار. |
| This chat link lists more websites than it may. Remove some and save again. | يضم رابط الدردشة هذا مواقع أكثر من المسموح به. أزل بعضها ثم احفظ مرة أخرى. |
| That corner is not available. Choose one from the list. | هذا الموضع غير متاح. اختر واحدًا من القائمة. |
| That theme is not available. Choose one from the list. | هذا المظهر غير متاح. اختر واحدًا من القائمة. |
| Choose an accent colour. | اختر اللون المميِّز. |
| Write the colour as # and six digits or letters from a to f, for example #2563eb. | اكتب اللون بالعلامة # متبوعة بستة أرقام أو أحرف من a إلى f، مثل #2563eb. |
| This greeting is too long. Shorten it and save again. | هذه التحية طويلة جدًا. اختصرها ثم احفظ مرة أخرى. |
| This greeting has characters that cannot be saved. Paste it as plain text and try again. | تحوي هذه التحية رموزًا لا يمكن حفظها. الصقها نصًا عاديًا وحاول مرة أخرى. |
| This workspace has reached the number of website chat links its plan allows. Nothing was created. | بلغت مساحة العمل هذه عدد روابط دردشة الموقع الذي تسمح به باقتها، ولم يُنشأ شيء. |

**WhatsApp refusals**

| English | Arabic |
| --- | --- |
| Choose an agent for this number. | اختر وكيلًا لهذا الرقم. |
| This agent is not in this workspace. Reload the page and choose again. | هذا الوكيل ليس في مساحة العمل هذه. أعد تحميل الصفحة واختر من جديد. |
| A WhatsApp ID must be 1 to 64 digits, and digits only. | يجب أن يتكون معرّف واتساب من رقم واحد إلى 64 رقمًا، وأرقام فقط. |
| This display number is too long. Shorten it and save again. | رقم العرض هذا طويل جدًا. اختصره ثم احفظ مرة أخرى. |
| Enter the access token. | أدخل رمز الوصول. |
| This access token is too long. Check that it was copied correctly. | رمز الوصول هذا طويل جدًا. تأكد من نسخه بشكل صحيح. |
| Take this number out of service before changing it. | أخرج هذا الرقم من الخدمة قبل تغييره. |
| Add an access token before putting this number into service. | أضف رمز وصول قبل إدخال هذا الرقم في الخدمة. |
| This number is already connected, so it cannot be added again. | هذا الرقم متصل بالفعل، فلا يمكن إضافته مرة أخرى. |
| This workspace has reached the number of WhatsApp numbers its plan allows. Nothing was created. | بلغت مساحة العمل هذه عدد أرقام واتساب الذي تسمح به باقتها، ولم يُنشأ شيء. |

**Creating a workspace**

| English | Arabic |
| --- | --- |
| Enter a workspace name of up to 200 characters, on one line. | اكتب اسمًا لمساحة العمل لا يتجاوز 200 حرف، في سطر واحد. |
| That workspace slug is already taken. Choose a different one. | هذا المعرّف المختصر لمساحة العمل مستخدم بالفعل. اختر معرّفًا آخر. |
| Use lowercase letters, numbers and single hyphens for the workspace slug, between 3 and 63 characters. | استخدم في المعرّف المختصر لمساحة العمل أحرفًا لاتينية صغيرة وأرقامًا وشرطات مفردة، بين 3 و63 حرفًا. |

**The visitor chat (hosted page and widget)**

| English | Arabic |
| --- | --- |
| Write a message before sending it. | اكتب رسالة قبل إرسالها. |
| This message is too long. Shorten it and send it again. | هذه الرسالة طويلة جدًا. اختصرها ثم أرسلها مرة أخرى. |
| This message is still being answered. Please wait for the reply. | ما زال الرد على هذه الرسالة جاريًا. انتظر الرد من فضلك. |
| This message could not be answered. Send a new message to try again. | تعذّر الرد على هذه الرسالة. أرسل رسالة جديدة للمحاولة مرة أخرى. |

## 9. Risks and open issues

- **Reloading the tab that started indexing ends it.** Observed live: the browser aborts the request, the server cancels, and the document reads "Indexing stopped", needing another index. A person who presses Index and reloads loses that attempt. Backend ask in section 7.
- **A colour in the band around `#2e8b57`** passes the widget's rule (black text) but falls back to Nabra's look on the chat page, by your specification. The settings screen's checks describe the widget only.
- **Startup JavaScript has 380 B left** under the 145,000 target. 38.4 and 38.5 should watch it.
- **Synthetic records left in Northwind:** the agent and website chat "P38.3 Visitor Desk", the "P38.3 …" documents and a few public conversations. Each e2e run adds its "E2E …" workspace, as before.
- **The native reading** of the 94 strings in section 8 is still to come.

## 10. Proposed checkpoint commit message

```
feat: say how knowledge indexing went, read agent channels from the agent, and give the chat page the workspace's look and the visitor's language

Knowledge shows the last index attempt and the file size. File, agent, channel
and workspace refusals are worded by code in English and Arabic. The agent
screens no longer read the two channel lists. The widget settings offer Start
and End corners, and the chat link can carry a language. The hosted page shows
the workspace's name, greeting, accent and theme without a flash, and decides
its language before first paint from ?lang= or the browser.

The e2e session state is now ignored at any depth.
```

## Revision 1

Your native reading changed seven Arabic values, and nothing else: no code, no English and no message id. The plan-limit refusals now read as "the maximum in its plan". No test asserted the old Arabic.

These rows replace the same English rows in section 8:

| English | Arabic |
| --- | --- |
| This workspace has reached the number of knowledge documents its plan allows. Nothing was saved. | بلغت مساحة العمل هذه الحد الأقصى لمستندات المعرفة في باقتها، ولم يُحفظ شيء. |
| This workspace has reached the number of AI agents its plan allows. Nothing was saved. | بلغت مساحة العمل هذه الحد الأقصى لوكلاء الذكاء الاصطناعي في باقتها، ولم يُحفظ شيء. |
| This workspace has reached the number of website chat links its plan allows. Nothing was created. | بلغت مساحة العمل هذه الحد الأقصى لروابط دردشة الموقع في باقتها، ولم يُنشأ شيء. |
| This workspace has reached the number of WhatsApp numbers its plan allows. Nothing was created. | بلغت مساحة العمل هذه الحد الأقصى لأرقام واتساب في باقتها، ولم يُنشأ شيء. |
| This workspace has used all the AI usage its plan allows. | استنفدت مساحة العمل هذه حصة الذكاء الاصطناعي المسموح بها في باقتها. |
| Automatic follows each visitor's browser. Choosing a language adds it to the link. | الخيار التلقائي يتبع لغة متصفح كل زائر، واختيار لغة محددة يضيفها إلى الرابط. |
| A WhatsApp ID must be 1 to 64 digits, and digits only. | يجب أن يتكون معرّف واتساب من أرقام فقط، من رقم واحد إلى 64 رقمًا. |

The gates were run again for the checkpoint; the results are in the commit's confirmation.
