import sys
import os

# Debug: Print current working directory and Python path
print(f"Current working directory: {os.getcwd()}")
print(f"Current file location: {os.path.abspath(__file__)}")
print(f"Directory contents: {os.listdir('.')}")

# Add the current directory to Python path
current_dir = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, current_dir)
print(f"Added to Python path: {current_dir}")

# Check if static_qa_data.py exists
static_qa_path = os.path.join(current_dir, 'static_qa_data.py')
print(f"static_qa_data.py exists: {os.path.exists(static_qa_path)}")

import eventlet
eventlet.monkey_patch()

from application import application, socketio

if __name__ == "__main__":
    socketio.run(application) 