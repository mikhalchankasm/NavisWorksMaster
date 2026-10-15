import unittest

from scripts.cleanup_merged_work import (
    KEEP,
    REMOVE,
    PullRequest,
    Worktree,
    branch_decision,
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


if __name__ == "__main__":
    unittest.main()
