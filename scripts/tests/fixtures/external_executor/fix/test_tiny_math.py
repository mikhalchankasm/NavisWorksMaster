import unittest

from tiny_math import clamp


class ClampTests(unittest.TestCase):
    def test_below_lower_bound(self) -> None:
        self.assertEqual(clamp(-2, 0, 10), 0)

    def test_inside_interval(self) -> None:
        self.assertEqual(clamp(4, 0, 10), 4)

    def test_above_upper_bound(self) -> None:
        self.assertEqual(clamp(12, 0, 10), 10)


if __name__ == "__main__":
    unittest.main()
