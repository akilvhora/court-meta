pipeline {
    agent any

    environment {
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        NEXUS_URL  = 'http://server:8082'
        NEXUS_REPO = 'court-meta-raw'
        // InnoSetup compiler — adjust path if installed elsewhere
        ISCC = 'C:\\Program Files (x86)\\Inno Setup 6\\ISCC.exe'
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
                    // Self-contained single-file exe — no .NET runtime required on client
                    bat 'dotnet publish CourtMetaAPI.csproj --configuration Release --output publish\\ --no-build'
                }
            }
        }

        stage('Pack Extension') {
            steps {
                // Retrieve the RSA private key from Jenkins credentials (Secret File)
                withCredentials([file(credentialsId: 'court-meta-extension-key', variable: 'EXT_KEY_FILE')]) {
                    // Pack .crx and write the extension ID to a file
                    bat """
                        powershell -ExecutionPolicy Bypass -File installer\\pack-extension.ps1 ^
                            -ExtensionDir extension ^
                            -KeyFile "%EXT_KEY_FILE%" ^
                            -OutputCrx CourtMetaAPI\\publish\\court-meta.crx ^
                            -OutputIdFile CourtMetaAPI\\publish\\extension-id.txt
                    """
                }
            }
        }

        stage('Build Installer') {
            steps {
                script {
                    // Use PowerShell to read the ID — avoids bat echo pollution and BOM issues
                    def extId = bat(
                        script: '@powershell -Command "(Get-Content CourtMetaAPI\\publish\\extension-id.txt -Raw).Trim()"',
                        returnStdout: true
                    ).trim()
                    def version = "1.0.${env.BUILD_NUMBER}"

                    echo "Extension ID : ${extId}"
                    echo "Version      : ${version}"

                    // Single-line invocation — no ^ continuation to misparse
                    bat "\"${env.ISCC}\" /dExtensionID=\"${extId}\" /dVersionStr=\"${version}\" /O\"CourtMetaAPI\\publish\" installer\\court-meta-setup.iss"
                }
            }
        }

        stage('Archive') {
            steps {
                archiveArtifacts artifacts: 'CourtMetaAPI/publish/court-meta-setup-*.exe', fingerprint: true
                archiveArtifacts artifacts: 'CourtMetaAPI/publish/court-meta.crx',         fingerprint: true
            }
        }

        stage('Upload to Nexus') {
            steps {
                withCredentials([usernamePassword(
                    credentialsId: 'nexus-credentials',
                    usernameVariable: 'NEXUS_USER',
                    passwordVariable: 'NEXUS_PASS'
                )]) {
                    script {
                        def version = "1.0.${env.BUILD_NUMBER}"
                        def base    = "${env.NEXUS_URL}/repository/${env.NEXUS_REPO}/court-meta/${version}"

                        // Upload Windows installer
                        bat "curl -fsSL -u %NEXUS_USER%:%NEXUS_PASS% --upload-file CourtMetaAPI\\publish\\court-meta-setup-${version}.exe ${base}/court-meta-setup-${version}.exe"

                        // Upload packed Chrome extension
                        bat "curl -fsSL -u %NEXUS_USER%:%NEXUS_PASS% --upload-file CourtMetaAPI\\publish\\court-meta.crx ${base}/court-meta.crx"
                    }
                }
            }
        }
    }

    post {
        success {
            echo 'Build successful. Installer and CRX uploaded to Nexus.'
        }
        failure {
            echo 'Build failed. Check the console output above.'
        }
        always {
            cleanWs()
        }
    }
}
