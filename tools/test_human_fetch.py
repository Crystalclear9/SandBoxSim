"""Source retrieval must stay local and must not fetch the complete archive."""
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

spec=importlib.util.spec_from_file_location('human_fetch',Path(__file__).with_name('fetch-human-assets.py'))
fetch=importlib.util.module_from_spec(spec)
spec.loader.exec_module(fetch)


class HumanFetchTests(unittest.TestCase):
    def test_source_paths_cannot_escape_the_project_cache(self):
        with self.assertRaises(ValueError):fetch.safe_path('../outside.obj')
        self.assertEqual(fetch.safe_path('hair/short01/short01.obj').parent.name,'short01')
        with self.assertRaises(ValueError):fetch.request('https://example.com/unrelated.zip')

    def test_zip_tail_is_a_bounded_range_request(self):
        archive=fetch.ArchiveRanges('unused',280737770)
        archive.seek(-22,2)
        with patch.object(fetch,'request',return_value=(b'x'*22,206,{'Content-Range':'bytes 280737748-280737769/280737770'})) as request:
            self.assertEqual(archive.read(),b'x'*22)
            self.assertEqual(request.call_args.args[1],{'Range':'bytes=280737748-280737769'})
            self.assertEqual(archive.tell(),280737770)

    def test_server_must_honour_the_requested_range(self):
        archive=fetch.ArchiveRanges('unused',100)
        with patch.object(fetch,'request',return_value=(b'x'*100,200,{})):
            with self.assertRaises(ValueError):archive.read(10)
        with self.assertRaises(ValueError):archive.seek(-101,2)


if __name__=='__main__':
    unittest.main()
