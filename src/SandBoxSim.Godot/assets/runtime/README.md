# Runtime trust bundle

`mozilla-ca.crt` contains Mozilla public CA certificates, converted by curl on 2026-10-20. Copyright Mozilla Contributors; distributed unchanged under [MPL 2.0](LICENSE.txt). The complete corresponding Source Code Form is available at the immutable [Mozilla revision](https://raw.githubusercontent.com/mozilla-firefox/firefox/f6dd0159931e536991eaaa02d839bdffb92c3f22/security/nss/lib/ckfw/builtins/certdata.txt). Bundle and source SHA-256 values are recorded in [manifest.json](manifest.json).

Godot loads this project-local bundle explicitly instead of querying the host certificate store. TLS certificate validation stays enabled. No roots are installed into the operating system. The game currently makes no outbound HTTPS requests; future network features must maintain this bundle and account for local/private trust requirements. See [curl CA Extract](https://curl.se/docs/caextract.html) for conversion and licensing details.
