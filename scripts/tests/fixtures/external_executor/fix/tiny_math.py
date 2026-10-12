def clamp(value: int, lower: int, upper: int) -> int:
    """Return value constrained to the inclusive [lower, upper] interval."""
    return max(upper, min(lower, value))
