#!/bin/bash
# Simple HTTP server for the frontend
# Usage: ./serve.sh [port]

PORT=${1:-8080}

echo "Starting HTTP server on port $PORT..."
echo "Open http://localhost:$PORT in your browser"
echo "Press Ctrl+C to stop"
echo ""

# Try Python 3 first, then Python 2, then fallback to other options
if command -v python3 &> /dev/null; then
    python3 -m http.server $PORT
elif command -v python &> /dev/null; then
    python -m SimpleHTTPServer $PORT
elif command -v php &> /dev/null; then
    php -S localhost:$PORT
else
    echo "No suitable HTTP server found. Please install Python 3 or PHP."
    echo "Alternatively, open index.html directly in your browser."
    exit 1
fi

