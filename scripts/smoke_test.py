"""Exercise a compiled proxy against an isolated OpenAI-compatible HTTP server."""
import http.server
import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request

BINARY = str(Path(sys.argv[1]).resolve())
REQUESTS = []


class Upstream(http.server.BaseHTTPRequestHandler):
    def log_message(self, *_args):
        pass

    def do_GET(self):
        assert self.path == '/v1/models', self.path
        assert self.headers.get('Authorization') == 'Bearer smoke-secret'
        data = json.dumps({'data': [{'id': 'test-model', 'created': 0}]}).encode()
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(data)

    def do_POST(self):
        assert self.path == '/v1/chat/completions', self.path
        assert self.headers.get('Authorization') == 'Bearer smoke-secret'
        data = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
        REQUESTS.append(data)
        self.send_response(200)
        self.send_header('Content-Type', 'text/event-stream' if data.get('stream') else 'application/json')
        self.end_headers()
        if data.get('stream'):
            for delta, finish in [({'role': 'assistant', 'content': 'Hello'}, None), ({}, 'stop')]:
                chunk = {'id': 'chat-smoke', 'model': 'test-model',
                         'choices': [{'index': 0, 'delta': delta, 'finish_reason': finish}]}
                self.wfile.write(('data: ' + json.dumps(chunk) + '\n\n').encode())
                self.wfile.flush()
            self.wfile.write(b'data: [DONE]\n\n')
        else:
            message = {'role': 'assistant', 'content': 'Hello'}
            finish = 'stop'
            if data.get('tools'):
                message = {'role': 'assistant', 'content': None, 'tool_calls': [
                    {'id': 'call-test', 'type': 'function', 'function': {
                        'name': 'weather', 'arguments': '{"city":"Berlin"}'}}]}
                finish = 'tool_calls'
            response = {'id': 'chat-smoke', 'model': 'test-model',
                        'choices': [{'index': 0, 'message': message, 'finish_reason': finish}],
                        'usage': {'prompt_tokens': 5, 'completion_tokens': 1, 'total_tokens': 6}}
            self.wfile.write(json.dumps(response).encode())


def free_port():
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        return sock.getsockname()[1]


def fetch(base, path, payload=None):
    req = urllib.request.Request(base + path, data=None if payload is None else json.dumps(payload).encode(),
                                 headers={'Content-Type': 'application/json', 'x-api-key': 'smoke-secret'})
    with urllib.request.urlopen(req, timeout=10) as response:
        return response.read().decode()


def ready(base, child=None):
    for _ in range(200):
        if child is not None and child.poll() is not None:
            raise AssertionError(f'Proxy exited: {child.returncode}')
        try:
            assert fetch(base, '/health') == 'OK'
            return
        except (OSError, AssertionError):
            time.sleep(0.05)
    raise AssertionError('Proxy did not become ready')


def run():
    assert 'anthropic-proxy' in subprocess.check_output([BINARY, '--version'], text=True)
    assert '--daemon' in subprocess.check_output([BINARY, '--help'], text=True)
    with http.server.ThreadingHTTPServer(('127.0.0.1', 0), Upstream) as upstream, tempfile.TemporaryDirectory() as directory:
        thread = threading.Thread(target=upstream.serve_forever, daemon=True)
        thread.start()
        env = {k: v for k, v in os.environ.items() if k not in (
            'UPSTREAM_BASE_URL', 'ANTHROPIC_PROXY_BASE_URL', 'UPSTREAM_API_KEY', 'OPENROUTER_API_KEY',
            'COMPLETION_MODEL', 'REASONING_MODEL', 'ANTHROPIC_PROXY_MODEL_MAP',
            'UPSTREAM_API_KEY_PASSTHROUGH', 'HTTP_PROXY', 'HTTPS_PROXY', 'ALL_PROXY')}
        env['NO_PROXY'] = '127.0.0.1,localhost'
        env['USERPROFILE' if os.name == 'nt' else 'HOME'] = directory
        port = free_port()
        base = f'http://127.0.0.1:{port}'
        config = Path(directory) / 'config with spaces.env'
        config.write_text(f'UPSTREAM_BASE_URL=http://127.0.0.1:{upstream.server_port}\n'
                          f'UPSTREAM_API_KEY_PASSTHROUGH=true\nANTHROPIC_PROXY_BIND=127.0.0.1\nPORT={port}\n'
                          'ANTHROPIC_PROXY_MODEL_MAP=client-model=test-model\n')
        args = [BINARY, '--config', str(config)]
        child = subprocess.Popen(args, env=env, cwd=directory)
        try:
            ready(base, child)
            models = json.loads(fetch(base, '/v1/models'))
            assert models['data'][0]['id'] == 'test-model', models
            request = {'model': 'client-model', 'max_tokens': 64,
                       'messages': [{'role': 'user', 'content': 'Hello'}]}
            reply = json.loads(fetch(base, '/v1/messages', request))
            assert reply['content'][0]['text'] == 'Hello', reply
            assert reply['usage'] == {'input_tokens': 5, 'output_tokens': 1}, reply
            stream = fetch(base, '/v1/messages', dict(request, stream=True))
            assert 'event: message_start' in stream and 'event: message_stop' in stream, stream
            assert 'Hello' in stream, stream
            tool = {'name': 'weather', 'description': 'Weather', 'input_schema': {
                'type': 'object', 'properties': {'city': {'type': 'string'}}}}
            reply = json.loads(fetch(base, '/v1/messages', dict(request, tools=[tool])))
            assert reply['stop_reason'] == 'tool_use', reply
            assert reply['content'][0]['input'] == {'city': 'Berlin'}, reply
            assert all(req['model'] == 'test-model' for req in REQUESTS), REQUESTS
            assert '# TYPE' in fetch(base, '/metrics')
        finally:
            child.terminate()
            child.wait(timeout=10)
        pid = str(Path(directory) / 'daemon with spaces.pid')
        try:
            subprocess.run(args + ['--daemon', '--pid-file', pid], cwd=directory, env=env, check=True, timeout=20)
            ready(base)
            subprocess.run([BINARY, 'status', '--pid-file', pid], check=True, timeout=10)
            if os.name == 'nt':
                duplicate = subprocess.run(args + ['--daemon', '--pid-file', pid], cwd=directory, env=env, timeout=20)
                assert duplicate.returncode != 0, 'Duplicate daemon start succeeded'
        finally:
            subprocess.run([BINARY, 'stop', '--pid-file', pid], check=True, timeout=10)
        assert not Path(pid).exists()
        status = subprocess.run([BINARY, 'status', '--pid-file', pid], timeout=10)
        assert status.returncode != 0
        if os.name == 'nt':
            # Configuration failures must be reported to the parent, not as a successful daemon start.
            config.write_text('UPSTREAM_BASE_URL=invalid\n')
            failure = subprocess.run(args + ['--daemon', '--pid-file', pid], cwd=directory, env=env, timeout=20)
            assert failure.returncode != 0
            assert not Path(pid).exists()
        upstream.shutdown()
    print('PASS: CLI, health, models, messages, SSE, tools, model mapping, key passthrough, metrics, daemon lifecycle')


if __name__ == '__main__':
    run()
