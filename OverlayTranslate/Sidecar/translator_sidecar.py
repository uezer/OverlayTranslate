import argparse
import json
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


class _ReusingHTTPServer(ThreadingHTTPServer):
    allow_reuse_address = True


def load_argos():
    import argostranslate.translate  # type: ignore
    return argostranslate.translate


class Handler(BaseHTTPRequestHandler):
    def log_message(self, format, *args):  # suppress per-request access logs
        pass

    def log_error(self, format, *args):  # still allow error logs
        sys.stderr.write("[sidecar] " + (format % args) + "\n")
        sys.stderr.flush()

    def _send_json(self, status, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/health":
            self._send_json(200, {"status": "ok"})
            return
        if self.path == "/languages":
            translator = load_argos()
            languages = []
            for language in translator.get_installed_languages():
                languages.append({"code": language.code, "name": language.name})
            self._send_json(200, languages)
            return

        self._send_json(404, {"error": "not found"})

    def do_POST(self):
        if self.path != "/translate":
            self._send_json(404, {"error": "not found"})
            return

        length = int(self.headers.get("Content-Length", 0))
        raw_body = self.rfile.read(length)
        payload = json.loads(raw_body.decode("utf-8") or "{}")

        texts = payload.get("q", [])
        if isinstance(texts, str):
            texts = [texts]

        source = payload.get("source", "auto")
        target = payload.get("target")
        if not target:
            self._send_json(400, {"error": "target is required"})
            return

        translator = load_argos()
        installed_languages = translator.get_installed_languages()
        installed_codes = {language.code for language in installed_languages}

        translated = []
        detected_language = None
        for text in texts:
            if source == "auto":
                detected_language = translator.detect_language(text)
                if detected_language is None:
                    raise RuntimeError("Unable to detect source language")
                source_code = detected_language.code
            else:
                source_code = source

            if source_code not in installed_codes or target not in installed_codes:
                raise RuntimeError("Required Argos language package is not installed")

            translation = translator.translate(text, source_code, target)
            translated.append(translation)

        response = {"translatedText": translated}
        if detected_language is not None:
            response["detectedLanguage"] = {"language": detected_language.code, "confidence": 100.0}
        self._send_json(200, response)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=0)  # 0 = OS picks a free port
    args = parser.parse_args()

    try:
        server = _ReusingHTTPServer((args.host, args.port), Handler)
    except OSError as exc:
        print(f"FAILED:{exc}", file=sys.stderr, flush=True)
        sys.exit(1)

    actual_port = server.server_address[1]
    # Signal to the host process that we are ready and on which port
    print(f"LISTENING:{actual_port}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
