"""Captures what one Git build prints for each refusal and outcome Trag reads by Git's own words, as the file of the
corpus that FailureTextCorpusTests runs through Trag's classifiers. Run it once per Git build, with that build's git.exe:
    python -I scripts/capture-failure-text-corpus.py "C:/Program Files/Git/cmd/git.exe"
The file is tests/Trag.Infrastructure.UnitTests/Git/Diagnostics/FailureTextCorpus/<version>.txt, named after git --version.
Each situation runs the command Trag runs, with the options and environment GitCommandLine and
TransportEnvironment give it, and LocalGitPreset's for the reads and writes Trag gives it to, no system or global configuration and none of the launching shell's GIT_ variables. The
hook needs the build's sh and the signing situations its ssh-keygen and gpg, which MinGit lacks. Windows only, with port
47811 free; the work folder reads C:/corpus in the file, and a NUL byte, which a -z record holds, reads ^@.
"""
import ctypes
import http.server
import os
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import threading
from pathlib import Path

GIT = Path(sys.argv[1]).resolve()
TOOLS = GIT.parent.parent / "usr" / "bin"
CORPUS = Path(__file__).resolve().parent.parent / "tests" / "Trag.Infrastructure.UnitTests" / "Git" / "Diagnostics" / "FailureTextCorpus"
PORT = 47811
SERVER = f"http://127.0.0.1:{PORT}"

# TransportEnvironment.Create and Setup for a remote named origin, then the fetch, push and remote branch deletion.
TRANSFER_ENVIRONMENT = {
    "GIT_NO_LAZY_FETCH": "1", "GCM_INTERACTIVE": "Never", "GCM_GUI_PROMPT": "false", "SSH_ASKPASS_REQUIRE": "never",
    "GIT_LFS_FORCE_PROGRESS": "1", "GIT_NO_REPLACE_OBJECTS": "1", "GIT_SSH_VARIANT": "ssh",
    "GIT_SSH_COMMAND": "ssh -oBatchMode=yes -oConnectTimeout=15 -oServerAliveInterval=15 -oServerAliveCountMax=2"}
PROTOCOLS = ["-c", "protocol.allow=never", "-c", "protocol.file.allow=always", "-c", "protocol.ssh.allow=always",
             "-c", "protocol.https.allow=always", "-c", "protocol.http.allow=always", "-c", "protocol.git.allow=always"]
TRANSFER = [*PROTOCOLS, "-c", "fetch.writeCommitGraph=false", "-c", "push.autoSetupRemote=false", "-c", "submodule.recurse=false",
            "-c", "remote.origin.mirror=false"]
FETCH = [*TRANSFER, "fetch", "--atomic", "--no-prune", "--no-write-fetch-head", "--no-auto-gc",
         "--progress", "--refmap=", "--", "origin", "+refs/heads/*:refs/remotes/origin/*"]
PUSH = [*TRANSFER, "push", "--porcelain", "--progress", "--no-follow-tags"]
# GitCloneOperation's clone, which has neither a remote nor its configuration yet.
CLONE_ENVIRONMENT = {"GIT_NO_LAZY_FETCH": "1", "GCM_INTERACTIVE": "Never", "GCM_GUI_PROMPT": "false", "SSH_ASKPASS_REQUIRE": "never",
                     "GIT_LFS_FORCE_PROGRESS": "1", "GIT_LFS_SKIP_SMUDGE": "1"}
CLONE = [*PROTOCOLS, "-c", "submodule.recurse=false", "clone", "--progress", "--no-recurse-submodules", "--"]
# LocalGitPreset's Reading and Writing, which every local command gets before its own arguments.
READING = ["-c", "protocol.allow=never"]
WRITING = ["-c", "protocol.allow=never", "-c", "core.fsmonitor=false"]
LOCAL = {"GIT_NO_LAZY_FETCH": "1"}
# CommitDetailsReader's show of one commit with Git's check of its signature.
SHOW = [*WRITING, "show", "-s", "--no-renames", "--no-notes", "--no-show-signature", "--encoding=UTF-8", "-z",
        "--format=%G?%x00%GS%x00%H%x00%P%x00%aN%x00%aE%x00%aI%x00%B"]
# StashWriter's check before a stash with untracked files, CommitWriter's commit and VerifiedMerge's merge.
DRY_RUN = ["add", "--dry-run", "--ignore-missing", "--pathspec-from-file=-", "--pathspec-file-nul"]
COMMIT = ["commit", "--file=-", "--cleanup=verbatim"]
MERGE = ["-c", "submodule.recurse=false", "-c", "branch.main.mergeOptions=", "merge", "--no-ff", "--commit", "--no-squash",
         "--no-autostash", "--no-edit", "--cleanup=verbatim", "-m", "Merge", "--strategy=ort", "--no-rerere-autoupdate",
         "--no-overwrite-ignore", "--"]

work = Path(tempfile.mkdtemp(prefix="trag-corpus-"))
(work / "global.gitconfig").write_text("[user]\n\tname = Corpus\n\temail = corpus@example.invalid\n[commit]\n\tgpgSign = false\n")
environment = {key: value for key, value in os.environ.items() if not key.startswith("GIT_") and key not in ("SSH_ASKPASS", "SSH_AUTH_SOCK")}
environment.update(GIT_TERMINAL_PROMPT="0", GIT_OPTIONAL_LOCKS="0", LC_ALL="C", GIT_CONFIG_NOSYSTEM="1",
                   GIT_CONFIG_GLOBAL=str(work / "global.gitconfig"))
# Fixed dates give every build the same commit ids, so the files of two builds differ only where Git words a refusal
# differently.
environment.update(GIT_AUTHOR_DATE="2026-01-01T00:00:00Z", GIT_COMMITTER_DATE="2026-01-01T00:00:00Z")
rows = {}


def git(folder, *arguments, literal=False, stdin=b"", variables=None):
    options = ["--literal-pathspecs"] if literal else []
    return subprocess.run([str(GIT), "--no-pager", "-c", "core.longpaths=true", "-c", "i18n.commitEncoding=UTF-8", *options, *arguments],
                          cwd=folder, input=stdin,
                          env={**environment, **(variables or {})}, capture_output=True, timeout=120,
                          creationflags=subprocess.CREATE_NO_WINDOW)


def setup(folder, *arguments, **options):
    result = git(folder, *arguments, **options)
    if result.returncode != 0:
        sys.exit(f"git {' '.join(arguments)} failed in {folder}:\n{result.stderr.decode(errors='replace')}")
    return result.stdout.decode().strip()


def capture(situation, folder, *arguments, **options):
    rows[situation] = git(folder, *arguments, **options)


def write(path, text):
    with open(path, "w", newline="\n") as file:
        file.write(text)


def posix(path):
    return str(path).replace("\\", "/")


def repository(name):
    folder = work / name
    setup(work, "init", "-q", "--initial-branch=main", posix(folder))
    return folder


class Remote(http.server.BaseHTTPRequestHandler):
    # A repository under /private/ asks every client to sign in and refuses any credentials, one under /forbidden/ is
    # refused to every client, and any other is not there.
    def do_GET(self):
        private = self.path.startswith("/private/")
        self.send_response(401 if private else 403 if self.path.startswith("/forbidden/") else 404)
        if private:
            self.send_header("WWW-Authenticate", 'Basic realm="corpus"')
        self.send_header("Content-Length", "0")
        self.end_headers()

    do_POST = do_GET

    def log_message(self, *arguments):
        pass


server = http.server.ThreadingHTTPServer(("127.0.0.1", PORT), Remote)
threading.Thread(target=server.serve_forever, daemon=True).start()

# Branch: switch and switch -c as BranchChange runs them, and BranchDeleteWriter's delete of a branch Git keeps.
branch = repository("branch")
write(branch / "a.txt", "one\n")
setup(branch, "add", "a.txt")
setup(branch, "commit", "-qm", "one")
setup(branch, "switch", "-qc", "other")
write(branch / "a.txt", "other\n")
write(branch / "t.txt", "other\n")
setup(branch, "add", "a.txt", "t.txt")
setup(branch, "commit", "-qm", "other")
setup(branch, "switch", "-q", "main")
write(branch / "a.txt", "local\n")
capture("branch-switch-local-changes", branch, "switch", "--no-guess", "other")
setup(branch, "checkout", "-q", "--", "a.txt")
write(branch / "t.txt", "untracked\n")
capture("branch-switch-untracked", branch, "switch", "--no-guess", "other")
(branch / "t.txt").unlink()
capture("branch-switch-create-exists", branch, "switch", "--no-guess", "-c", "other", "HEAD")
write(branch / ".git" / "index.lock", "")
capture("branch-switch-index-lock", branch, "switch", "--no-guess", "other")
(branch / ".git" / "index.lock").unlink()
capture("branch-switch-unknown-ref", branch, "switch", "--no-guess", "nowhere")
capture("branch-delete-not-fully-merged", branch, *WRITING, "branch", "-d", "other", literal=True, variables=LOCAL)
setup(branch, "worktree", "add", "-q", posix(work / "linked"), "other")
capture("branch-switch-used-by-worktree", branch, "switch", "--no-guess", "other")
capture("branch-delete-used-by-worktree", branch, *WRITING, "branch", "-d", "other", literal=True, variables=LOCAL)

# Stash: push, apply and drop as StashWriter runs them, and its check before a stash with untracked files.
stash = repository("stash")
write(stash / "a.txt", "one\n")
setup(stash, "add", "a.txt")
setup(stash, "commit", "-qm", "one")
write(stash / "a.txt", "two\n")
setup(stash, "stash", "push", "-q")
write(stash / "a.txt", "three\n")
capture("stash-apply-local-changes", stash, "stash", "apply", "stash@{0}", literal=True)
setup(stash, "checkout", "-q", "--", "a.txt")
setup(stash, "stash", "drop", "-q")
write(stash / "new.txt", "new\n")
setup(stash, "stash", "push", "-q", "--include-untracked")
write(stash / "new.txt", "different\n")
capture("stash-apply-untracked-exists", stash, "stash", "apply", "stash@{0}", literal=True)
(stash / "new.txt").unlink()
write(stash / ".git" / "index.lock", "")
capture("stash-apply-index-lock", stash, "stash", "apply", "stash@{0}", literal=True)
(stash / ".git" / "index.lock").unlink()
write(stash / "a.txt", "four\n")
write(stash / ".git" / "index.lock", "")
capture("stash-push-index-lock", stash, "stash", "push")
(stash / ".git" / "index.lock").unlink()
setup(stash, "checkout", "-q", "--", "a.txt")
capture("stash-drop-unknown", stash, "stash", "drop", "stash@{5}", literal=True)
# A reserved device name with an extension, which Windows 11 writes as an ordinary file.
device = "\\\\?\\" + str(stash / "CON.txt")
write(device, "device\n")
capture("stash-untracked-unindexable", stash, *DRY_RUN, literal=True, stdin=b"CON.txt\0")
os.remove(device)
# A file another program holds open without sharing it.
write(stash / "held.txt", "held\n")
held = ctypes.windll.kernel32.CreateFileW(str(stash / "held.txt"), 0x80000000, 0, None, 3, 0x80, None)
capture("stash-untracked-held-open", stash, *DRY_RUN, literal=True, stdin=b"held.txt\0")
ctypes.windll.kernel32.CloseHandle(held)
# A push with only that untracked file changed, an apply over a commit that changed the stashed line, and a drop that
# names the entry it dropped.
capture("stash-push-only-untracked", stash, *WRITING, "-c", "i18n.logOutputEncoding=UTF-8", "stash", "push", variables=LOCAL)
write(stash / "a.txt", "five\n")
setup(stash, "stash", "push", "-q")
write(stash / "a.txt", "six\n")
setup(stash, "commit", "-qam", "six")
capture("stash-apply-conflicts", stash, *WRITING, "stash", "apply", "stash@{0}", literal=True, variables=LOCAL)
setup(stash, "reset", "-q", "--hard")
capture("stash-drop-other-entry", stash, *WRITING, "stash", "drop", "stash@{1}", literal=True, variables=LOCAL)

# Transfers: a local bare remote for the pushes, the server above for HTTP.
hub = work / "hub.git"
setup(work, "init", "-q", "--bare", "--initial-branch=main", posix(hub))
one = work / "one"
setup(work, "clone", "-q", posix(hub), posix(one))
write(one / "f.txt", "1\n")
setup(one, "add", "f.txt")
setup(one, "commit", "-qm", "1")
setup(one, "push", "-q", "origin", "main")
two = work / "two"
setup(work, "clone", "-q", posix(hub), posix(two))
write(one / "f.txt", "2\n")
setup(one, "commit", "-qam", "2")
setup(one, "push", "-q", "origin", "main")
write(two / "g.txt", "mine\n")
setup(two, "add", "g.txt")
setup(two, "commit", "-qm", "mine")
head = setup(two, "rev-parse", "HEAD")
capture("transport-push-fetch-first", two, *PUSH, "--recurse-submodules=no", "--no-force", "--no-force-if-includes", "--", "origin",
        head + ":refs/heads/main", variables=TRANSFER_ENVIRONMENT)
setup(two, "fetch", "-q", "origin")
capture("transport-push-non-fast-forward", two, *PUSH, "--recurse-submodules=no", "--no-force", "--no-force-if-includes", "--", "origin",
        head + ":refs/heads/main", variables=TRANSFER_ENVIRONMENT)
remote = setup(two, "rev-parse", "origin/main")
capture("transport-push-delete-current-branch", two, *PUSH, "--force-with-lease=refs/heads/main:" + remote, "--delete", "--", "origin",
        "refs/heads/main", variables=TRANSFER_ENVIRONMENT)
# FetchOperation's fetch for a pull asks for the upstream as well.
capture("pull-upstream-deleted", two, *FETCH, "refs/heads/gone", variables=TRANSFER_ENVIRONMENT)
# The picker's look at a bare repository under the user's safe.bareRepository=explicit.
capture("probe-bare-explicit", hub, "-c", "safe.bareRepository=explicit", "rev-parse", "--is-inside-git-dir", "--is-bare-repository",
        "--show-toplevel", literal=True)
fetcher = repository("fetcher")
setup(fetcher, "remote", "add", "origin", SERVER + "/private/trag.git")
capture("transport-http-401-no-username", fetcher, *FETCH, variables=TRANSFER_ENVIRONMENT)
setup(fetcher, "remote", "set-url", "origin", f"http://alice@127.0.0.1:{PORT}/private/trag.git")
capture("transport-http-401-no-password", fetcher, *FETCH, variables=TRANSFER_ENVIRONMENT)
setup(fetcher, "remote", "set-url", "origin", SERVER + "/private/trag.git")
capture("transport-http-401-refused", fetcher, "-c", "credential.helper=!f() { echo username=alice; echo password=wrong; }; f", *FETCH,
        variables=TRANSFER_ENVIRONMENT)
setup(fetcher, "remote", "set-url", "origin", SERVER + "/acme/none.git")
capture("transport-http-404-missing", fetcher, *FETCH, variables=TRANSFER_ENVIRONMENT)
setup(fetcher, "remote", "set-url", "origin", "http://127.0.0.1:1/acme/trag.git")
capture("transport-http-connection-refused", fetcher, *FETCH, variables=TRANSFER_ENVIRONMENT)


def clone(situation, address, *configuration):
    capture(situation, work, *configuration, *CLONE, address, posix(work / situation), literal=True, variables=CLONE_ENVIRONMENT)


clone("clone-http-401-no-username", SERVER + "/private/trag.git")
clone("clone-http-401-refused", SERVER + "/private/trag.git", "-c", "credential.helper=!f() { echo username=alice; echo password=wrong; }; f")
clone("clone-http-403-forbidden", SERVER + "/forbidden/trag.git")
clone("clone-http-404-missing", SERVER + "/acme/none.git")
clone("clone-http-connection-refused", "http://127.0.0.1:1/acme/trag.git")
clone("clone-unresolvable-host", "http://corpus.invalid/trag.git")
# A folder another program filled after Trag checked it was empty.
(work / "clone-target-exists").mkdir()
write(work / "clone-target-exists" / "taken.txt", "taken\n")
clone("clone-target-exists", posix(hub))
server.shutdown()

# Signing: a commit and a merge commit Git cannot sign, and refusals that are not about the signature.
signing = repository("signing")
write(signing / "a.txt", "one\n")
write(signing / "b.txt", "one\n")
setup(signing, "add", "a.txt", "b.txt")
setup(signing, "commit", "-qm", "one")
setup(signing, "switch", "-qc", "feature")
write(signing / "b.txt", "feature\n")
setup(signing, "commit", "-qam", "feature")
setup(signing, "switch", "-qc", "conflict", "main")
write(signing / "a.txt", "conflict\n")
setup(signing, "commit", "-qam", "conflict")
setup(signing, "switch", "-q", "main")
write(signing / "a.txt", "main\n")
setup(signing, "commit", "-qam", "main")
write(signing / "b.txt", "two\n")
setup(signing, "add", "b.txt")
missing = ["-c", "commit.gpgSign=true", "-c", "gpg.format=ssh", "-c", "gpg.ssh.program=" + posix(work / "missing-keygen"),
           "-c", "user.signingKey=" + posix(work / "key.pub")]
capture("commit-ssh-program-missing", signing, *missing, *COMMIT, stdin=b"two\n")
subprocess.run([str(TOOLS / "ssh-keygen.exe"), "-q", "-t", "ed25519", "-N", "a passphrase", "-f", str(work / "locked-key")],
               check=True, capture_output=True, timeout=60, creationflags=subprocess.CREATE_NO_WINDOW)
capture("commit-ssh-key-passphrase", signing, "-c", "commit.gpgSign=true", "-c", "gpg.format=ssh",
        "-c", "gpg.ssh.program=" + posix(TOOLS / "ssh-keygen.exe"), "-c", "user.signingKey=" + posix(work / "locked-key.pub"),
        *COMMIT, stdin=b"two\n")
(work / "gnupg").mkdir()
capture("commit-gpg-no-secret-key", signing, "-c", "commit.gpgSign=true", "-c", "gpg.format=openpgp",
        "-c", "gpg.program=" + posix(TOOLS / "gpg.exe"), "-c", "user.signingKey=DEADBEEFDEADBEEF", *COMMIT, stdin=b"two\n",
        variables={"GNUPGHOME": posix(work / "gnupg")})
(work / "hooks").mkdir()
write(work / "hooks" / "pre-commit", "#!/bin/sh\necho 'pre-commit: tests failed' >&2\nexit 1\n")
capture("commit-hook-refuses-signed", signing, *missing, "-c", "core.hooksPath=" + posix(work / "hooks"), *COMMIT, stdin=b"two\n")
setup(signing, "reset", "-q", "--hard")
capture("merge-signing-refused", signing, *missing, *MERGE, setup(signing, "rev-parse", "feature"),
        variables={"GIT_MERGE_AUTOEDIT": "no"})
setup(signing, "merge", "--abort")
capture("merge-conflicts-while-signing", signing, *missing, *MERGE, setup(signing, "rev-parse", "conflict"),
        variables={"GIT_MERGE_AUTOEDIT": "no"})
setup(signing, "merge", "--abort")
# TagWriter's annotated tag Git cannot sign, and its lightweight tag, for which Git wants a message once it signs tags.
signed_tags = ["-c", "tag.gpgSign=true", *missing[2:]]
head = setup(signing, "rev-parse", "HEAD")
capture("tag-signing-refused", signing, *signed_tags, *WRITING, "tag", "--file=-", "--cleanup=verbatim", "signed", head, literal=True,
        stdin=b"signed\n", variables=LOCAL)
capture("tag-needs-message", signing, *signed_tags, *WRITING, "tag", "light", head, literal=True, variables={**LOCAL, "GIT_EDITOR": ":"})


# Commits someone else signed, whose signatures Git does not get to check here: it cannot start the program that
# checks them, or has no allowed signers for an SSH key. Fixed dates keep their ids the same for every build.
def signed(armor):
    tree = setup(signing, "rev-parse", "HEAD^{tree}")
    commit = (f"tree {tree}\nparent {head}\nauthor Corpus <corpus@example.invalid> 1767225600 +0000\n"
              f"committer Corpus <corpus@example.invalid> 1767225600 +0000\ngpgsig -----BEGIN {armor} SIGNATURE-----\n"
              f" c2lnbmF0dXJl\n -----END {armor} SIGNATURE-----\n\nsigned\n")
    return setup(signing, "hash-object", "-t", "commit", "-w", "--stdin", stdin=commit.encode())


write(work / "allowed_signers", "")
capture("signature-openpgp-program-missing", signing, "-c", "gpg.program=" + posix(work / "missing-gpg"), *SHOW, signed("PGP"), "--",
        literal=True, variables=LOCAL)
capture("signature-ssh-program-missing", signing, "-c", "gpg.ssh.program=" + posix(work / "missing-keygen"),
        "-c", "gpg.ssh.allowedSignersFile=" + posix(work / "allowed_signers"), *SHOW, signed("SSH"), "--", literal=True, variables=LOCAL)
capture("signature-ssh-no-allowed-signers", signing, "-c", "gpg.ssh.program=" + posix(TOOLS / "ssh-keygen.exe"), *SHOW, signed("SSH"), "--",
        literal=True, variables=LOCAL)

# Pick and ownership: PickWriter's revert of a change the branch has already undone, and the first look at a repository
# that the snapshot and the picker take, at one Git refuses for its owner. Git's test switch makes it refuse as for
# another account, without naming the accounts, which differ per computer.
pick = repository("pick")
write(pick / "a.txt", "one\n")
setup(pick, "add", "a.txt")
setup(pick, "commit", "-qm", "one")
write(pick / "a.txt", "two\n")
setup(pick, "commit", "-qam", "two")
change = setup(pick, "rev-parse", "HEAD")
write(pick / "a.txt", "one\n")
setup(pick, "commit", "-qam", "undo two")
capture("revert-nothing-to-commit", pick, *WRITING, "revert", "--no-edit", "--no-rerere-autoupdate", change,
        variables={**LOCAL, "GIT_EDITOR": ":"})
other_owner = {"GIT_TEST_ASSUME_DIFFERENT_OWNER": "1"}
capture("open-dubious-ownership", pick, *READING, "rev-parse", "--is-bare-repository", "--show-toplevel", "--absolute-git-dir",
        "--path-format=absolute", "--git-common-dir", literal=True, variables={**LOCAL, **other_owner})
capture("probe-dubious-ownership", pick, "rev-parse", "--is-inside-git-dir", "--is-bare-repository", "--show-toplevel", literal=True,
        variables=other_owner)


def text(output):
    return re.sub(r"[^\s'\"]*trag-corpus-[^/\\\s'\"]*", "C:/corpus", output.decode("utf-8", "replace").replace("\r\n", "\n")).rstrip("\n").replace("\0", "^@")


version = setup(work, "--version").removeprefix("git version ")
CORPUS.mkdir(parents=True, exist_ok=True)
with open(CORPUS / (version + ".txt"), "w", encoding="utf-8", newline="\n") as corpus:
    for situation, result in rows.items():
        corpus.write(f"=== {situation}\n")
        for stream, output in (("stderr", text(result.stderr)), ("stdout", text(result.stdout))):
            corpus.write(f"--- {stream}\n" + (output + "\n" if output else ""))


def writable(function, path, _):
    os.chmod(path, stat.S_IWRITE)
    function(path)


shutil.rmtree(work, onexc=writable)
print(CORPUS / (version + ".txt"))
