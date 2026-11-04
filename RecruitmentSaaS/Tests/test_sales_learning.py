"""Offline checks for full-history semantic review coverage; no provider calls."""
import importlib.util
from pathlib import Path
import sys
import unittest

TOOLS = Path(__file__).resolve().parents[1] / "tools"
sys.path.insert(0, str(TOOLS))
spec = importlib.util.spec_from_file_location("sales_learning", TOOLS / "learn_sales_conversations.py")
learning = importlib.util.module_from_spec(spec)
spec.loader.exec_module(learning)


class CoverageTests(unittest.TestCase):
    def setUp(self):
        self.batch = [{"ref": "c1", "messages": [[1, "C", "question"], [2, "H", "answer"]]},
                      {"ref": "c2", "messages": [[1, "C", "[uninterpreted media type 4]"]]}]

    @staticmethod
    def row(ref, evidence):
        return dict(ref=ref, countries=["unknown"], quality="learning", issue="question",
                    learning="Answer the actual question", evidence=evidence)

    def test_exact_coverage(self):
        self.assertEqual(2, len(learning.validate({"reviews": [self.row("c1", [1, 2]),
                                                               self.row("c2", [1])]}, self.batch)))

    def test_omitted_chat_is_rejected(self):
        with self.assertRaises(ValueError):
            learning.validate({"reviews": [self.row("c1", [1])]}, self.batch)

    def test_duplicate_chat_cannot_replace_missing_chat(self):
        with self.assertRaises(ValueError):
            learning.validate({"reviews": [self.row("c1", [1]), self.row("c1", [1])]}, self.batch)

    def test_cross_chat_evidence_is_rejected(self):
        with self.assertRaises(ValueError):
            learning.validate({"reviews": [self.row("c1", [1]), self.row("c2", [2])]}, self.batch)

    def test_unconfigured_country_is_rejected(self):
        row = self.row("c1", [1])
        row["countries"] = ["unsupported destination"]
        with self.assertRaises(ValueError):
            learning.validate({"reviews": [row, self.row("c2", [1])]}, self.batch)

    def test_schema_requires_each_chat_and_scopes_evidence(self):
        schema = learning.batch_schema(self.batch)["properties"]["reviews"]
        self.assertEqual(["c1", "c2"], schema["required"])
        self.assertFalse(schema["additionalProperties"])
        self.assertEqual([1], schema["properties"]["c2"]["properties"]["evidence"]["items"]["enum"])


if __name__ == "__main__":
    unittest.main()
