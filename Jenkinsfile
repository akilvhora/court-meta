pipeline {
    agent any

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
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
    }

    post {
        success {
            echo 'Build successful. Artifacts archived.'
        }
        failure {
            echo 'Build failed. Check the console output above.'
        }
        always {
            cleanWs()
        }
    }
}
