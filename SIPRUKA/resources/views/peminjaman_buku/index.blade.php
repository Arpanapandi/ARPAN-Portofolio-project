<x-app-layout>
    <x-slot name="header">
        <h2 class="font-semibold text-xl">Daftar Peminjaman Buku</h2>
    </x-slot>

    <div class="p-6">
        <a href="{{ route('peminjaman-buku.create') }}" class="bg-blue-500 text-black px-4 py-2 rounded">
            Tambah Peminjaman Buku
        </a>

        @if (session('success'))
            <div class="mt-4 p-2 bg-green-200 text-green-800 rounded">
                {{ session('success') }}
            </div>
        @endif

        <table class="mt-4 w-full border">
            <thead>
                <tr>
                    <th class="border p-2">User</th>
                    <th class="border p-2">Buku</th>
                    <th class="border p-2">Tanggal Pinjam</th>
                    <th class="border p-2">Tanggal Kembali</th>
                    <th class="border p-2">Status</th>
                    <th class="border p-2">Aksi</th>
                </tr>
            </thead>
            <tbody>
                @foreach ($data as $item)
                <tr>
                    <td class="border p-2">{{ $item->user->nama }}</td>
                    <td class="border p-2">{{ $item->buku->judul }}</td>
                    <td class="border p-2">{{ $item->tanggal_pinjam }}</td>
                    <td class="border p-2">{{ $item->tanggal_kembali ?? '-' }}</td>
                    <td class="border p-2 capitalize">{{ $item->status }}</td>
                    <td class="border p-2">
                        <a href="{{ route('peminjaman-buku.edit', $item->id) }}" class="text-blue-600">Edit</a>

                        <form action="{{ route('peminjaman-buku.destroy', $item->id) }}" method="POST" class="inline">
                            @csrf
                            @method('DELETE')
                            <button class="text-red-600" onclick="return confirm('Hapus data?')">Hapus</button>
                        </form>
                    </td>
                </tr>
                @endforeach
            </tbody>
        </table>
    </div>
</x-app-layout>
