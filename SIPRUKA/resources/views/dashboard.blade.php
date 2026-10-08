<x-app-layout>
    <x-slot name="header">
        <h2 class="font-semibold text-xl text-gray-800 leading-tight">
            {{ __('Dashboard') }}
        </h2>
    </x-slot>

    <div class="py-12">
        <div class="max-w-7xl mx-auto sm:px-6 lg:px-8">
            <div class="bg-white overflow-hidden shadow-sm sm:rounded-lg">
                <div class="p-6 text-gray-900">
                    {{ __("You're logged in!") }}
                </div>
            </div>

            <div class="mt-8 grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6">
                <!-- Buku Card -->
                <div class="bg-white overflow-hidden shadow-sm sm:rounded-lg">
                    <div class="p-6">
                        <div class="flex items-center">
                            <svg class="w-8 h-8 text-gray-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.746 0 3.332.477 4.5 1.253v13C19.832 18.477 18.246 18 16.5 18c-1.746 0-3.332.477-4.5 1.253"></path>
                            </svg>
                            <div class="ml-4">
                                <p class="text-sm font-medium text-gray-600">Buku</p>
                                <p class="text-2xl font-semibold text-gray-900">{{ \App\Models\Buku::count() }}</p>
                            </div>
                        </div>
                        <div class="mt-4">
                            <a href="{{ route('buku.index') }}" class="text-blue-600 hover:text-blue-500 text-sm font-medium">Kelola Buku →</a>
                        </div>
                    </div>
                </div>

                <!-- Ruangan Card -->
                <div class="bg-white overflow-hidden shadow-sm sm:rounded-lg">
                    <div class="p-6">
                        <div class="flex items-center">
                            <svg class="w-8 h-8 text-gray-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M19 21V5a2 2 0 00-2-2H7a2 2 0 00-2 2v16m14 0h2m-2 0h-5m-9 0H3m2 0h5M9 7h1m-1 4h1m4-4h1m-1 4h1m-5 10v-5a1 1 0 011-1h2a1 1 0 011 1v5m-4 0h4"></path>
                            </svg>
                            <div class="ml-4">
                                <p class="text-sm font-medium text-gray-600">Ruangan</p>
                                <p class="text-2xl font-semibold text-gray-900">{{ \App\Models\Ruangan::count() }}</p>
                            </div>
                        </div>
                        <div class="mt-4">
                            <a href="{{ route('ruangan.index') }}" class="text-blue-600 hover:text-blue-500 text-sm font-medium">Kelola Ruangan →</a>
                        </div>
                    </div>
                </div>

                <!-- Peminjaman Buku Card -->
                <div class="bg-white overflow-hidden shadow-sm sm:rounded-lg">
                    <div class="p-6">
                        <div class="flex items-center">
                            <svg class="w-8 h-8 text-gray-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"></path>
                            </svg>
                            <div class="ml-4">
                                <p class="text-sm font-medium text-gray-600">Peminjaman Buku</p>
                                <p class="text-2xl font-semibold text-gray-900">{{ \App\Models\PeminjamanBuku::count() }}</p>
                            </div>
                        </div>
                        <div class="mt-4">
                            <a href="{{ route('peminjaman-buku.index') }}" class="text-blue-600 hover:text-blue-500 text-sm font-medium">Kelola Peminjaman →</a>
                        </div>
                    </div>
                </div>

                <!-- Peminjaman Ruangan Card -->
                <div class="bg-white overflow-hidden shadow-sm sm:rounded-lg">
                    <div class="p-6">
                        <div class="flex items-center">
                            <svg class="w-8 h-8 text-gray-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 7V3a2 2 0 012-2h4a2 2 0 012 2v4m-6 4v10m0 0l-2-2m2 2l2-2m6-6v6m0 0l2 2m-2-2l-2 2"></path>
                            </svg>
                            <div class="ml-4">
                                <p class="text-sm font-medium text-gray-600">Peminjaman Ruangan</p>
                                <p class="text-2xl font-semibold text-gray-900">{{ \App\Models\PeminjamanRuangan::count() }}</p>
                            </div>
                        </div>
                        <div class="mt-4">
                            <a href="{{ route('peminjaman-ruangan.index') }}" class="text-blue-600 hover:text-blue-500 text-sm font-medium">Kelola Peminjaman →</a>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
</x-app-layout>
