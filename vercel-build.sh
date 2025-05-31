#!/bin/bash

# Install dependencies
pip install -r requirements.txt

# Run database migrations
python migrations.py

echo "Build completed successfully!" 