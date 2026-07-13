import com.android.build.api.dsl.LibraryExtension
import org.jetbrains.kotlin.gradle.dsl.JvmTarget

plugins {
    id("com.android.library")
}

group = "com.unity_kit"
version = "0.9.1"

extensions.configure<LibraryExtension> {
    namespace = "com.unity_kit"
    compileSdk = 35

    defaultConfig {
        minSdk = 21
        consumerProguardFiles("consumer-rules.pro")
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

}

kotlin {
    compilerOptions {
        jvmTarget = JvmTarget.JVM_17
    }
}

dependencies {
    implementation("androidx.lifecycle:lifecycle-common:2.8.7")
    implementation("androidx.lifecycle:lifecycle-process:2.8.7")
    implementation("androidx.annotation:annotation:1.9.1")
    compileOnly(group = "", name = "unity-classes", ext = "jar") // provided by Unity at runtime via flatDir

    val lifecycleProject = rootProject.findProject(":flutter_plugin_android_lifecycle")
    if (lifecycleProject != null) {
        compileOnly(lifecycleProject)
    }
}
