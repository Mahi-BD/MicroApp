# Privacy Policy for MicroApp

**Last updated: 31 August 2026**

MicroApp is a Windows tray utility published by RampsBD. This policy explains, plainly, what
happens to your data when you use it.

## The short version

**MicroApp collects nothing.** There is no MicroApp account, no MicroApp server, and no telemetry,
analytics, crash reporting or usage tracking of any kind. Nothing you type, copy, capture, record or
write is sent to the developer, ever. The application works fully offline.

A few features in Notes *can* talk to the internet, but every one of them is switched off until you
turn it on and supply your own credentials, and each one talks to a service **you** chose and
**your** account — never to us. Those features are listed below.

## What stays on your computer

All of the following is stored locally on your own PC and never leaves it:

- **Your settings**, in the standard Windows per-user application settings file, including any API
  keys or tokens you enter for the optional features below.
- **Your notes**, as plain `.txt` files in MicroApp's Notes folder, plus the archive and trash lists.
- **Screenshots, GIFs and videos** you capture, saved to the folder you choose.
- **Image editor assets** (logos and stamps you add to the asset library).

MicroApp reads your **clipboard** when you ask it to paste as keystrokes, and reads **screen pixels**
when you ask it to capture, record, or run OCR. This data is processed in memory on your machine and
is not stored or transmitted by MicroApp. Text recognition uses the OCR engine built into Windows 10
and 11 — it runs locally and sends nothing anywhere.

## The optional features that use the internet

Each of these is disabled by default. Each requires you to enter your own key, token or account. If
you never enable them, MicroApp makes no network connections at all.

| Feature | What is sent | Where it goes |
|---|---|---|
| AI note actions (Grammar, Ask AI, Bangla → English) | The text of the note you run the action on, plus your instruction | The AI provider **you** select, using **your** API key: OpenAI, OpenRouter, Google Gemini, or MiMo |
| Bangla phonetic typing | The single word you are currently typing, as a lookup query | The [string.bd](https://string.bd) dictionary service, using **your** token |
| Note sync | Your notes and their metadata | A Firebase project created under **your own** Google account — Google Identity Toolkit for sign-in and Cloud Firestore for storage |

In every case the data goes directly from your computer to that provider. It does not pass through
any server operated by RampsBD or by this project — there is no such server. Once your data reaches
a third-party provider it is governed by that provider's own privacy policy and by the terms of the
account you hold with them.

You can stop any of this at any time by clearing the relevant key or token in Settings, or by turning
note sync off.

## What we do not do

- We do not collect, receive, store or have access to any of your data.
- We do not sell, rent or share data with anyone, because we do not have any.
- We do not show advertising and we include no advertising or analytics SDKs.
- We do not profile you or build any kind of user identity.

## Children

MicroApp is a general-purpose desktop utility. It is not directed at children and it knowingly
collects no personal information from anyone, including children.

## Updates and downloads

MicroApp checks for new versions and downloads releases from its public GitHub repository. Such a
request tells GitHub your IP address, as any web request does; it is handled under
[GitHub's Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).
No identifier of you or your installation is attached to those requests by MicroApp.

## Changes to this policy

If this policy changes, the updated version will be published in this repository and the date at the
top will change. The history of every change is public in the repository's commit log.

## Contact

Questions about this policy, or about privacy in MicroApp, can be raised as an issue at
<https://github.com/Mahi-BD/MicroApp/issues>.

## Source

MicroApp is open source. If you would rather verify all of the above than take our word for it, the
complete source code is at <https://github.com/Mahi-BD/MicroApp>.
