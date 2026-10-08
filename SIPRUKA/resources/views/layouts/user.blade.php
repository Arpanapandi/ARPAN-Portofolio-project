<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>@yield('title', 'User Dashboard')</title>
    <link href="https://cdn.jsdelivr.net/npm/tailwindcss@3.3.2/dist/tailwind.min.css" rel="stylesheet">
</head>
<body class="bg-gray-100">
    <div class="flex min-h-screen">
        {{-- Sidebar --}}
        <div class="w-64 bg-white shadow-md">
            <h2 class="text-2xl font-bold p-4">User Panel</h2>
            <nav class="flex flex-col p-4 space-y-2">
                <a href="{{ route('user.dashboard') }}" class="hover:bg-gray-200 p-2 rounded">Dashboard</a>
                <a href="{{ route('buku.index') }}" class="hover:bg-gray-200 p-2 rounded">Buku</a>
                <a href="{{ route('ruangan.index') }}" class="hover:bg-gray-200 p-2 rounded">Ruangan</a>
                <a href="{{ route('peminjaman-buku.index') }}" class="hover:bg-gray-200 p-2 rounded">Peminjaman Buku</a>
                <a href="{{ route('peminjaman-ruangan.index') }}" class="hover:bg-gray-200 p-2 rounded">Peminjaman Ruangan</a>
            </nav>
        </div>

        {{-- Main Content --}}
        <div class="flex-1 p-6">
            <nav class="flex justify-end mb-4">
                <span class="mr-4">{{ auth()->user()->name }}</span>
                <form method="POST" action="{{ route('logout') }}">
                    @csrf
                    <button class="bg-red-500 text-white px-3 py-1 rounded">Logout</button>
                </form>
            </nav>
            <div class="bg-white p-6 rounded shadow">
                @yield('content')
            </div>
        </div>
    </div>
</body>
</html>
