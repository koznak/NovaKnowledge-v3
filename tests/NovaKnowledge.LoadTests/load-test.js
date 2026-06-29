import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  stages: [
    { duration: '30s', target: 20 },
    { duration: '1m', target: 50 },
    { duration: '30s', target: 0 },
  ],
  thresholds: {
    http_req_duration: ['p(95)<3000'],
    http_req_failed: ['rate<0.05'],
  },
};

const BASE_URL = __ENV.GATEWAY_URL || 'http://localhost:5000';

export default function () {
  const health = http.get(`${BASE_URL}/health`);
  check(health, { 'health ok': (r) => r.status === 200 });

  const search = http.post(
    `${BASE_URL}/api/search/query`,
    JSON.stringify({ query: 'PTO policy', tenantId: 'contoso', topK: 5, useHybrid: true }),
    { headers: { 'Content-Type': 'application/json' } }
  );
  check(search, { 'search ok': (r) => r.status === 200 });

  sleep(1);
}
