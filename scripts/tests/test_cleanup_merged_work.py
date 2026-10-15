import os
import shutil
import subprocess
import tempfile
import unittest

from scripts.cleanup_merged_work import (
    KEEP,
    REMOVE,
    PullRequest,
    Worktree,
    branch_decision,
    content_matches_main,
    worktree_decision,
)

TIP = "a" * 40
OTHER = "b" * 40


class BranchDecisionTests(unittest.TestCase):
    def test_merged_pull_request_with_identical_tip_is_removed(self):
        self.assertEqual(REMOVE, branch_decision("claude/x", TIP, PullRequest("MERGED", TIP), set())[0])

    def test_commits_after_the_merge_keep_the_branch(self):
        action, reason = branch_decision("claude/x", OTHER, PullRequest("MERGED", TIP), set())
        self.assertEqual(KEEP, action)
        self.assertIn("beyond the merged", reason)

    def test_open_or_closed_pull_request_keeps_the_branch(self):
        for state in ("OPEN", "CLOSED"):
            self.assertEqual(KEEP, branch_decision("claude/x", TIP, PullRequest(state, TIP), set())[0])

    def test_branch_without_a_pull_request_is_kept(self):
        self.assertEqual(KEEP, branch_decision("rs-1", TIP, None, set())[0])

    def test_checked_out_branch_is_kept(self):
        action, reason = branch_decision("claude/x", TIP, PullRequest("MERGED", TIP), {"claude/x"})
        self.assertEqual((KEEP, "checked out in a worktree"), (action, reason))

    def test_main_is_never_removed(self):
        self.assertEqual(KEEP, branch_decision("main", TIP, PullRequest("MERGED", TIP), set())[0])

    def test_branch_without_a_pull_request_whose_content_is_in_main_is_removed(self):
        action, reason = branch_decision("codex/291-silent-catches", TIP, None, set(), content_in_main=True)
        self.assertEqual((REMOVE, "every file it changed is a version origin/main has held"), (action, reason))

    def test_closed_or_superseded_branch_whose_content_is_in_main_is_removed(self):
        for pr in (PullRequest("CLOSED", TIP), PullRequest("MERGED", OTHER)):
            self.assertEqual(REMOVE, branch_decision("claude/x", TIP, pr, set(), content_in_main=True)[0])

    def test_open_pull_request_is_kept_even_when_its_content_is_in_main(self):
        action, reason = branch_decision("claude/x", TIP, PullRequest("OPEN", TIP), set(), content_in_main=True)
        self.assertEqual((KEEP, "pull request is open"), (action, reason))

    def test_checked_out_branch_is_kept_even_when_its_content_is_in_main(self):
        self.assertEqual(KEEP, branch_decision("claude/x", TIP, None, {"claude/x"}, content_in_main=True)[0])


def tree(path="D:/w/feature", head=TIP, branch="", is_main=False, is_current=False, dirty=False):
    return Worktree(path, head, branch, is_main, is_current, dirty)


class WorktreeDecisionTests(unittest.TestCase):
    def test_clean_worktree_whose_commit_is_in_main_is_removed(self):
        self.assertEqual(REMOVE, worktree_decision(tree(), [], True, False)[0])

    def test_clean_worktree_of_a_merged_branch_is_removed(self):
        self.assertEqual(REMOVE, worktree_decision(tree(branch="claude/x"), [], False, True)[0])

    def test_dirty_worktree_is_kept_even_when_its_commit_is_in_main(self):
        action, reason = worktree_decision(tree(dirty=True), [], True, True)
        self.assertEqual((KEEP, "has uncommitted changes"), (action, reason))

    def test_main_and_current_checkouts_are_kept(self):
        self.assertEqual(KEEP, worktree_decision(tree(is_main=True), [], True, True)[0])
        self.assertEqual(KEEP, worktree_decision(tree(is_current=True), [], True, True)[0])

    def test_keep_matches_the_path_regardless_of_slashes_and_case(self):
        action, reason = worktree_decision(tree("D:/GitHub/Exec/build-caeaf63"), ["d:\\github\\exec\\BUILD-caeaf63"], True, False)
        self.assertEqual((KEEP, "kept by --keep"), (action, reason))

    def test_another_tools_worktree_is_kept(self):
        action, reason = worktree_decision(tree("C:/Users/U/.codex/worktrees/x/Repo"), [], True, True)
        self.assertEqual((KEEP, "another tool's worktree"), (action, reason))

    def test_unmerged_work_is_kept(self):
        self.assertEqual(KEEP, worktree_decision(tree(), [], False, False)[0])

    def test_clean_worktree_whose_content_is_in_main_is_removed(self):
        action, reason = worktree_decision(tree(branch="codex/291"), [], False, False, content_in_main=True)
        self.assertEqual((REMOVE, "clean, and every file it changed is a version origin/main has held"), (action, reason))

    def test_dirty_worktree_is_kept_even_when_its_content_is_in_main(self):
        self.assertEqual(KEEP, worktree_decision(tree(dirty=True), [], False, False, content_in_main=True)[0])


class ContentMatchesMainTests(unittest.TestCase):
    """content_matches_main against a throwaway repository: the squash-merge case."""

    def setUp(self):
        self.repo = tempfile.mkdtemp(prefix="cleanup-merged-")
        self.addCleanup(shutil.rmtree, self.repo, ignore_errors=True)
        self.run_git("init", "-q", "-b", "main")
        self.run_git("config", "user.email", "test@example.invalid")
        self.run_git("config", "user.name", "test")
        self.commit("a.txt", "one\n", "base")
        self.run_git("switch", "-q", "-c", "feature")
        self.commit("a.txt", "two\n", "feature change")
        self.run_git("switch", "-q", "main")

    def run_git(self, *args):
        subprocess.run(["git", "-C", self.repo, *args], check=True, capture_output=True)

    def commit(self, name, text, message):
        with open(os.path.join(self.repo, name), "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
        self.run_git("add", name)
        self.run_git("commit", "-q", "-m", message)

    def test_unmerged_change_is_not_in_main(self):
        self.assertFalse(content_matches_main(self.repo, "feature", "main"))

    def test_the_same_change_squashed_into_main_counts_as_merged(self):
        self.commit("a.txt", "two\n", "squash of feature")
        self.assertTrue(content_matches_main(self.repo, "feature", "main"))

    def test_a_later_edit_in_main_keeps_the_branch(self):
        self.commit("a.txt", "three\n", "someone else's change")
        self.assertFalse(content_matches_main(self.repo, "feature", "main"))

    def test_an_intermediate_stack_branch_matches_an_earlier_squash_on_main(self):
        self.commit("a.txt", "two\n", "squash of feature (stack 1/2)")
        self.commit("a.txt", "three\n", "squash of the next PR (stack 2/2)")
        self.assertTrue(content_matches_main(self.repo, "feature", "main"))

    def test_a_deleted_file_counts_once_main_deleted_it_too(self):
        self.run_git("switch", "-q", "-c", "remover")
        self.run_git("rm", "-q", "a.txt")
        self.run_git("commit", "-q", "-m", "delete a")
        self.run_git("switch", "-q", "main")
        self.assertFalse(content_matches_main(self.repo, "remover", "main"))
        self.run_git("rm", "-q", "a.txt")
        self.run_git("commit", "-q", "-m", "delete a on main")
        self.assertTrue(content_matches_main(self.repo, "remover", "main"))


if __name__ == "__main__":
    unittest.main()
