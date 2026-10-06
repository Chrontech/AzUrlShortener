## Usage after deploy

### Create short url for mobile deep link

Request:
```
curl --fail-with-body -i \
  'https://api.mangomushroom-b47227cd.eastus2.azurecontainerapps.io/api/UrlCreate' \
  -H 'x-api-key: YOUR_API_KEY' \
  -H 'Content-Type: application/json' \
  --data '{
    "vanity": "mobile-test-001",
    "linkType": "mobile",
    "data": {
      "screen": "home",
      "source": "curl-test"
    }
  }'
```
Response:
```
{
  "shortUrl":"https://short.gochronicle.com/m/mobile-test-001",
  "longUrl":"https://portal.gochronicle.com/?site=download-chronicle%2F",
  "title":"",
  "linkType":"mobile",
  "data":{"screen":"home","source":"curl-test"}
}
```

### Get Data on Mobile

```
curl --fail-with-body -i \
  'https://short.gochronicle.com/resolve/mobile-test-001'
```