@echo off
echo Creating deployment directory...
if exist deployment rmdir /s /q deployment
mkdir deployment

echo Copying files...
copy application.py deployment\
copy wsgi.py deployment\
copy requirements.txt deployment\
copy config.py deployment\
copy models.py deployment\
copy migrations.py deployment\
copy static_qa_data.py deployment\
copy Procfile deployment\
copy .env_new deployment\

echo Copying modules directory...
xcopy modules deployment\modules\ /E /I /Y

echo Copying other directories...
xcopy static deployment\static\ /E /I /Y
xcopy templates deployment\templates\ /E /I /Y
xcopy .ebextensions deployment\.ebextensions\ /E /I /Y
xcopy .platform deployment\.platform\ /E /I /Y
xcopy data deployment\data\ /E /I /Y
xcopy uploads deployment\uploads\ /E /I /Y
xcopy Assets deployment\Assets\ /E /I /Y
xcopy instance deployment\instance\ /E /I /Y

echo Deployment directory created successfully!
echo Please create a ZIP file from the deployment directory manually. 