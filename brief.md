# Brief

The opening context and motivation for this project. Frozen unless explicitly
told otherwise.

## Purpose & Motivation

This repo contains a project to wrap the AppTaskInfo API in a CLI that can be
consumed by other applications & scripts to help provide visibility into what
the scripts and applications are doing without having to monitor the application
CLI/script/app UI. Full docs for the API are available locally in the `docs`
folder.

At its core, the AppTaskInfo API allows an application to post state of an
operation -- progress, running, line-by-line status, paused, requesting
attention/input -- and it's mostly stateless. One of the key points is that it
*doesn't* require the originating process to be running for the last-seen-state
to be displayed. There are lots of nuances in the API (see the docs above) but
this is the general gist. But one of the challenges is that 'call it from a
script', or 'call it from an agent hook' is somewhat problematic, and requires
the API caller to do a lot of housekeeping. It should be easy to leverage this
from scripts et al with simple CLIs -- a wrapper for the API as a baseline
expectation.

The catalysing scenario was "add hook scripts into agents such as Claude Code,
GitHub Copilot CLI, et al, and have them display status on the taskbar using
this API, allow handling of tool requests etc etc". The idea being that adding
them *directly* to these apps requires access to those tools, and since I don't
work at either Anthropic or GitHub isn't viable. Plus, there are so many agent
hosts that this wouldn't scale. When you include the script scenario -- where
scripts call this tool to display status -- it feels broadly useful.

It started out with a 'proof of concept' in this repo and it's now a functional
proof of concept, and it needs to be broadened out through a full implementation
covering real world scenarios, failure cases, testing, good ergonomics, etc.

## Assertions

1. Testability should follow 'write tests, see red, implement product code, see
   green, iterate' as our testing approach.
2. We should be distribution-system-agnostic -- MSIX, loose file, all should be
   possible, but a late-bound decision for us. We'll revisit *after* we've
   completed building the product.
