#!/bin/bash

# Install Python 3.9
curl -L https://raw.githubusercontent.com/pyenv/pyenv-installer/master/bin/pyenv-installer | bash
export PATH="$HOME/.pyenv/bin:$PATH"
eval "$(pyenv init -)"
pyenv install 3.9.18
pyenv global 3.9.18

# Install dependencies
python3.9 -m pip install --upgrade pip
python3.9 -m pip install -r requirements.txt 