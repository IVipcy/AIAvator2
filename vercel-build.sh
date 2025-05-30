#!/bin/bash

# Set Python version
export PYTHON_VERSION=3.9
export VERCEL_PYTHON_RUNTIME=3.9

# Install Python 3.9 using pyenv
if [ ! -d "$HOME/.pyenv" ]; then
    git clone https://github.com/pyenv/pyenv.git ~/.pyenv
    export PYENV_ROOT="$HOME/.pyenv"
    export PATH="$PYENV_ROOT/bin:$PATH"
    eval "$(pyenv init --path)"
    eval "$(pyenv init -)"
    pyenv install 3.9.18
    pyenv global 3.9.18
fi

# Verify Python version
python --version

# Install dependencies
python -m pip install --upgrade pip
python -m pip install -r requirements.txt 