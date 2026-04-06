pipeline {
    agent any

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        NEXUS_URL  = 'http://server:8082'
        NEXUS_REPO = 'court-meta-raw'
    }

    stages {

        stage('Checkout') {
            steps {
                checkout scm
            }
        }

        stage('Bundle Extension') {
            steps {
                dir('CourtMetaAPI') {
                    bat 'dotnet msbuild -t:BundleExtension -nologo -v:minimal'
                }
            }
        }

        stage('Build') {
            steps {
                dir('CourtMetaAPI') {
                    bat 'dotnet build CourtMetaAPI.csproj --configuration Release --no-incremental'
                }
            }
        }

        stage('Publish') {
            steps {
                dir('CourtMetaAPI') {
                    bat 'dotnet publish CourtMetaAPI.csproj --configuration Release --output publish\\ --no-build'
                }
            }
        }

        stage('Archive') {
            steps {
                archiveArtifacts artifacts: 'CourtMetaAPI/publish/**', fingerprint: true
                archiveArtifacts artifacts: 'CourtMetaAPI/wwwroot/court-meta-extension.zip', fingerprint: true
            }
        }

        stage('Upload to Nexus') {
            steps {
                withCredentials([usernamePassword(
                    credentialsId: 'nexus-credentials',
                    usernameVariable: 'NEXUS_USER',
                    passwordVariable: 'NEXUS_PASS'
                )]) {
                    // Zip the publish output for upload
                    bat 'powershell -Command "Compress-Archive -Path CourtMetaAPI\\publish\\* -DestinationPath CourtMetaAPI\\court-meta-api.zip -Force"'

                    // Upload API artifact
                    bat "curl -fsSL -u %NEXUS_USER%:%NEXUS_PASS% --upload-file CourtMetaAPI\\court-meta-api.zip %NEXUS_URL%/repository/%NEXUS_REPO%/court-meta-api/%BUILD_NUMBER%/court-meta-api.zip"

                    // Upload Chrome extension artifact
                    bat "curl -fsSL -u %NEXUS_USER%:%NEXUS_PASS% --upload-file CourtMetaAPI\\wwwroot\\court-meta-extension.zip %NEXUS_URL%/repository/%NEXUS_REPO%/court-meta-extension/%BUILD_NUMBER%/court-meta-extension.zip"
                }
            }
        }
    }

    post {
        success {
            echo 'Build successful. Artifacts archived and uploaded to Nexus.'
        }
        failure {
            echo 'Build failed. Check the console output above.'
        }
        always {
            cleanWs()
        }
    }
}
